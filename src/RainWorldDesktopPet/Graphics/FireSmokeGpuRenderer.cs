using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using SharpDX;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.RainWorld;
using Device = SharpDX.Direct3D11.Device;
using Buffer = SharpDX.Direct3D11.Buffer;

namespace RainWorldDesktopPet.Graphics
{
    /// <summary>
    /// Executes the installed FireSmoke fragment expression as a D3D11 compute
    /// shader. GPU readback stays on this worker thread; the GDI overlay only
    /// receives completed 128px masks and never waits for a graphics fence.
    /// </summary>
    public sealed class FireSmokeGpuRenderer : IDisposable
    {
        public const int RasterSize = 128;

        private const string ShaderSource = @"
cbuffer FireSmokeInput : register(b0)
{
    float2 center;
    float rotation;
    float size;
    float2 worldOrigin;
    float renderScale;
    float worldPadding;
    float2 screenSize;
    float vertexAlpha;
    float padding;
};
Texture2D<float> noiseTex : register(t0);
Texture2D<float> noiseTex2 : register(t1);
SamplerState noiseSampler : register(s0);
SamplerState noiseSampler2 : register(s1);
RWTexture2D<float4> outputMask : register(u0);

[numthreads(8, 8, 1)]
void Main(uint3 threadId : SV_DispatchThreadID)
{
    if (threadId.x >= 128 || threadId.y >= 128) return;
    const float rain = 0.5;
    float2 uv = (float2(threadId.xy) + 0.5) / 128.0;
    float localX = (uv.x - 0.5) * size;
    float localY = (uv.y - 0.5) * size;
    float sine;
    float cosine;
    sincos(radians(rotation), sine, cosine);
    float2 world = center + float2(localX * cosine - localY * sine,
        localX * sine + localY * cosine);
    float2 screen = world * renderScale - worldOrigin;
    float2 textCoord = float2(floor(screen.x) / screenSize.x,
        floor(screen.y - rain * 153.2) / screenSize.y);
    textCoord.y += 0.04;
    float dist = saturate(1.0 - length(uv - float2(0.5, 0.5)) * 2.0);
    float h = sin((1.77 * rain + noiseTex.SampleLevel(noiseSampler,
        float2(textCoord.x * 5.2, rain * .1 + textCoord.y * 2.6), 0).r * 3) * 6.28318530718) * .5 + .5;
    h *= sin((3.5 * rain + noiseTex.SampleLevel(noiseSampler,
        float2(textCoord.x * 12.2, rain * .25 + textCoord.y * 6.6), 0).r * 3) * 6.28318530718) * .5 + .5;
    h *= .5 + .5 * sin((noiseTex.SampleLevel(noiseSampler, uv, 0).r + rain) * 6.28318530718 * 3);
    h = lerp(h * dist, lerp(h, 1, lerp(.3, .8, vertexAlpha)), dist);
    h -= noiseTex2.SampleLevel(noiseSampler2,
        float2(textCoord.x * 15.2, rain * .1 + textCoord.y * 7.6), 0).r * lerp(.7, .3, vertexAlpha);
    outputMask[threadId.xy] = h * vertexAlpha < .35 ? float4(0, 0, 0, 0) : float4(1, 1, 1, 1);
}";

        [StructLayout(LayoutKind.Sequential)]
        private struct ShaderInput
        {
            public float CenterX;
            public float CenterY;
            public float Rotation;
            public float Size;
            public float WorldOriginX;
            public float WorldOriginY;
            public float RenderScale;
            public float WorldPadding;
            public float ScreenWidth;
            public float ScreenHeight;
            public float VertexAlpha;
            public float Padding;
        }

        private sealed class RenderJob
        {
            public object Owner;
            public int Layer;
            public int Generation;
            public ShaderInput Input;
        }

        private sealed class OwnerState
        {
            public int BackGeneration;
            public int FrontGeneration;
        }

        public sealed class CompletedMask
        {
            public object Owner;
            public int Layer;
            public int Generation;
            public byte[] Pixels;
            public bool Superseded;
        }

        private readonly BlockingCollection<RenderJob> jobs =
            new BlockingCollection<RenderJob>(new ConcurrentQueue<RenderJob>());
        private readonly ConcurrentQueue<CompletedMask> completed =
            new ConcurrentQueue<CompletedMask>();
        private readonly ConcurrentDictionary<object, OwnerState> latestGenerations =
            new ConcurrentDictionary<object, OwnerState>();
        private readonly AutoResetEvent initialized = new AutoResetEvent(false);
        private readonly Thread worker;
        private readonly FireSmokeShaderAssets assets;
        private volatile bool disposed;
        private Exception startupException;

        private FireSmokeGpuRenderer(FireSmokeShaderAssets assets)
        {
            this.assets = assets;
            worker = new Thread(WorkerMain);
            worker.IsBackground = true;
            worker.Name = "FireSmoke D3D11 worker";
            worker.Start();
            if (!initialized.WaitOne(5000))
                startupException = new TimeoutException(
                    "Timed out while starting the Direct3D 11 FireSmoke worker.");
        }

        public bool IsAvailable { get { return startupException == null && !disposed; } }

        public static FireSmokeGpuRenderer TryCreate(FireSmokeShaderAssets assets,
            out string status)
        {
            if (assets == null)
            {
                status = "Original FireSmoke GPU renderer unavailable: original textures were not loaded.";
                return null;
            }
            try
            {
                FireSmokeGpuRenderer result = new FireSmokeGpuRenderer(assets);
                if (!result.IsAvailable)
                {
                    status = "Original FireSmoke GPU renderer unavailable: " +
                        (result.startupException == null ? "startup timed out." :
                        result.startupException.Message);
                    result.Dispose();
                    return null;
                }
                status = "Original FireSmoke masks use asynchronous Direct3D 11 rendering.";
                return result;
            }
            catch (Exception exception)
            {
                status = "Original FireSmoke GPU renderer unavailable: " + exception.Message;
                return null;
            }
        }

        public void Queue(object owner, int layer, int generation, Vec2 center,
            double rotation, double size, Vec2 worldOrigin, double renderScale,
            int screenWidth, int screenHeight, double vertexAlpha)
        {
            if (!IsAvailable || owner == null) return;
            RenderJob job = new RenderJob();
            job.Owner = owner;
            job.Layer = layer;
            job.Generation = generation;
            job.Input.CenterX = (float)center.X;
            job.Input.CenterY = (float)center.Y;
            job.Input.Rotation = (float)rotation;
            job.Input.Size = (float)size;
            job.Input.WorldOriginX = (float)worldOrigin.X;
            job.Input.WorldOriginY = (float)worldOrigin.Y;
            job.Input.RenderScale = (float)renderScale;
            job.Input.ScreenWidth = Math.Max(1, screenWidth);
            job.Input.ScreenHeight = Math.Max(1, screenHeight);
            job.Input.VertexAlpha = (float)vertexAlpha;
            OwnerState state = latestGenerations.GetOrAdd(owner,
                delegate(object ignored) { return new OwnerState(); });
            if (layer == 0) Interlocked.Exchange(ref state.BackGeneration, generation);
            else Interlocked.Exchange(ref state.FrontGeneration, generation);
            try { jobs.Add(job); }
            catch (InvalidOperationException) { }
        }

        public bool TryTakeCompleted(out CompletedMask mask)
        {
            return completed.TryDequeue(out mask);
        }

        public void Release(object owner)
        {
            if (owner == null) return;
            OwnerState ignored;
            latestGenerations.TryRemove(owner, out ignored);
        }

        private void WorkerMain()
        {
            Device device = null;
            DeviceContext context = null;
            ComputeShader shader = null;
            Buffer inputBuffer = null;
            Texture2D noiseTexture = null;
            Texture2D noise2Texture = null;
            ShaderResourceView noiseView = null;
            ShaderResourceView noise2View = null;
            SamplerState sampler = null;
            SamplerState sampler2 = null;
            Texture2D output = null;
            UnorderedAccessView outputView = null;
            Texture2D staging = null;
            string initializationStep = "creating the Direct3D 11 device";
            try
            {
                device = new Device(DriverType.Hardware, DeviceCreationFlags.BgraSupport);
                context = device.ImmediateContext;
                initializationStep = "compiling the FireSmoke compute shader";
                ShaderBytecode byteCode = ShaderBytecode.Compile(ShaderSource, "Main",
                    "cs_5_0", ShaderFlags.OptimizationLevel3, EffectFlags.None);
                shader = new ComputeShader(device, byteCode);
                byteCode.Dispose();
                initializationStep = "creating the FireSmoke constant buffer";
                inputBuffer = new Buffer(device, Utilities.SizeOf<ShaderInput>(),
                    ResourceUsage.Dynamic, BindFlags.ConstantBuffer, CpuAccessFlags.Write,
                    ResourceOptionFlags.None, 0);
                initializationStep = "uploading the original noise texture";
                noiseTexture = CreateNoiseTexture(device, assets.CopyNoisePixels(),
                    assets.NoiseWidth, assets.NoiseHeight);
                initializationStep = "uploading the original noise2 texture";
                noise2Texture = CreateNoiseTexture(device, assets.CopyNoise2Pixels(),
                    assets.Noise2Width, assets.Noise2Height);
                noiseView = new ShaderResourceView(device, noiseTexture);
                noise2View = new ShaderResourceView(device, noise2Texture);
                initializationStep = "creating the FireSmoke sampler";
                sampler = new SamplerState(device, new SamplerStateDescription
                {
                    Filter = assets.UsesPointFiltering ? Filter.MinMagMipPoint : Filter.MinMagMipLinear,
                    AddressU = assets.UsesRepeatWrap ? TextureAddressMode.Wrap : TextureAddressMode.Clamp,
                    AddressV = assets.UsesRepeatWrap ? TextureAddressMode.Wrap : TextureAddressMode.Clamp,
                    AddressW = TextureAddressMode.Clamp,
                    ComparisonFunction = Comparison.Never,
                    MaximumAnisotropy = 1,
                    MinimumLod = 0,
                    MaximumLod = 0
                });
                sampler2 = new SamplerState(device, new SamplerStateDescription
                {
                    Filter = assets.UsesPointFiltering2 ? Filter.MinMagMipPoint : Filter.MinMagMipLinear,
                    AddressU = assets.UsesRepeatWrap2 ? TextureAddressMode.Wrap : TextureAddressMode.Clamp,
                    AddressV = assets.UsesRepeatWrap2 ? TextureAddressMode.Wrap : TextureAddressMode.Clamp,
                    AddressW = TextureAddressMode.Clamp,
                    ComparisonFunction = Comparison.Never,
                    MaximumAnisotropy = 1,
                    MinimumLod = 0,
                    MaximumLod = 0
                });
                initializationStep = "creating the FireSmoke GPU target";
                Texture2DDescription outputDescription = new Texture2DDescription
                {
                    Width = RasterSize,
                    Height = RasterSize,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.R8G8B8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.UnorderedAccess,
                    CpuAccessFlags = CpuAccessFlags.None,
                    OptionFlags = ResourceOptionFlags.None
                };
                output = new Texture2D(device, outputDescription);
                outputView = new UnorderedAccessView(device, output);
                initializationStep = "creating the asynchronous GPU readback target";
                outputDescription.Usage = ResourceUsage.Staging;
                outputDescription.BindFlags = BindFlags.None;
                outputDescription.CpuAccessFlags = CpuAccessFlags.Read;
                staging = new Texture2D(device, outputDescription);
            }
            catch (Exception exception)
            {
                startupException = new InvalidOperationException(
                    "Failed while " + initializationStep, exception);
                initialized.Set();
                DisposeResources(staging, outputView, output, sampler2, sampler, noise2View,
                    noiseView, noise2Texture, noiseTexture, inputBuffer, shader,
                    context, device);
                return;
            }
            initialized.Set();
            try
            {
                foreach (RenderJob job in jobs.GetConsumingEnumerable())
                {
                    if (disposed) break;
                    CompletedMask mask = new CompletedMask();
                    mask.Owner = job.Owner;
                    mask.Layer = job.Layer;
                    mask.Generation = job.Generation;
                    if (IsSuperseded(job))
                    {
                        mask.Superseded = true;
                        completed.Enqueue(mask);
                        continue;
                    }
                    try
                    {
                        DataBox mapped = context.MapSubresource(inputBuffer, 0,
                            MapMode.WriteDiscard, SharpDX.Direct3D11.MapFlags.None);
                        Utilities.Write(mapped.DataPointer, ref job.Input);
                        context.UnmapSubresource(inputBuffer, 0);
                        context.ComputeShader.Set(shader);
                        context.ComputeShader.SetConstantBuffer(0, inputBuffer);
                        context.ComputeShader.SetShaderResources(0, noiseView, noise2View);
                        context.ComputeShader.SetSampler(0, sampler);
                        context.ComputeShader.SetSampler(1, sampler2);
                        context.ComputeShader.SetUnorderedAccessView(0, outputView);
                        context.Dispatch(RasterSize / 8, RasterSize / 8, 1);
                        context.CopyResource(output, staging);
                        context.Flush();
                        DataBox outputData = context.MapSubresource(staging, 0,
                            MapMode.Read, SharpDX.Direct3D11.MapFlags.None);
                        try
                        {
                            mask.Pixels = new byte[RasterSize * RasterSize * 4];
                            for (int y = 0; y < RasterSize; y++)
                                Marshal.Copy(IntPtr.Add(outputData.DataPointer,
                                    y * outputData.RowPitch), mask.Pixels,
                                    y * RasterSize * 4, RasterSize * 4);
                        }
                        finally { context.UnmapSubresource(staging, 0); }
                        context.ComputeShader.SetUnorderedAccessView(0, null);
                        context.ComputeShader.SetShaderResources(0, null, null);
                        if (IsSuperseded(job))
                        {
                            mask.Pixels = null;
                            mask.Superseded = true;
                        }
                    }
                    catch
                    {
                        // Deliver an empty completion to release the cache's
                        // pending flag. The CPU path remains available.
                        mask.Pixels = null;
                    }
                    completed.Enqueue(mask);
                }
            }
            finally
            {
                DisposeResources(staging, outputView, output, sampler2, sampler, noise2View,
                    noiseView, noise2Texture, noiseTexture, inputBuffer, shader,
                    context, device);
            }
        }

        private bool IsSuperseded(RenderJob job)
        {
            OwnerState state;
            if (!latestGenerations.TryGetValue(job.Owner, out state)) return true;
            int generation = job.Layer == 0
                ? Interlocked.CompareExchange(ref state.BackGeneration, 0, 0)
                : Interlocked.CompareExchange(ref state.FrontGeneration, 0, 0);
            return generation != job.Generation;
        }

        private static Texture2D CreateNoiseTexture(Device device, float[] pixels,
            int width, int height)
        {
            DataStream stream = new DataStream(pixels.Length * sizeof(float), true, true);
            try
            {
                stream.WriteRange(pixels);
                stream.Position = 0;
                Texture2DDescription description = new Texture2DDescription
                {
                    Width = width,
                    Height = height,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.R32_Float,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Immutable,
                    BindFlags = BindFlags.ShaderResource,
                    CpuAccessFlags = CpuAccessFlags.None,
                    OptionFlags = ResourceOptionFlags.None
                };
                return new Texture2D(device, description,
                    new DataRectangle(stream.DataPointer, width * sizeof(float)));
            }
            finally { stream.Dispose(); }
        }

        private static void DisposeResources(params IDisposable[] resources)
        {
            for (int i = 0; i < resources.Length; i++)
                if (resources[i] != null) resources[i].Dispose();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            jobs.CompleteAdding();
            if (worker.IsAlive) worker.Join(2000);
            jobs.Dispose();
            latestGenerations.Clear();
        }
    }
}
