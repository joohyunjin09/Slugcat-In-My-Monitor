#include <windows.h>
#include <d3d11_1.h>
#include <d3dcompiler.h>
#include <dcomp.h>
#include <dxgi.h>
#include <wrl/client.h>
#include <array>
#include <cmath>
#include <cstring>
#include <memory>
#include <vector>

using Microsoft::WRL::ComPtr;

namespace
{
    constexpr int MaximumSurfaces = 8;
    constexpr UINT MaximumSmokeEffects = 256;
    constexpr ULONGLONG InactiveSurfaceReleaseMilliseconds = 10000;

    struct GpuSmokeEffect
    {
        float CenterX, CenterY, Rotation, BackSize, FrontSize;
        float BackRed, BackGreen, BackBlue, BackAlpha;
        float FrontRed, FrontGreen, FrontBlue, FrontAlpha;
        float Seed;
    };

    struct Surface
    {
        ComPtr<IDCompositionSurface> CompositionSurface;
        ComPtr<IDCompositionVisual> Visual;
        UINT Width = 0;
        UINT Height = 0;
        bool Active = false;
        ULONGLONG InactiveSince = 0;
    };

    struct EffectVertex
    {
        float X, Y, U, V;
        float Red, Green, Blue, Alpha;
        float Seed;
        float PixelScale, ScreenBiasX, ScreenBiasY;
    };

    constexpr char EffectShader[] = R"(
struct VertexInput { float2 position : POSITION; float2 uv : TEXCOORD0;
    float4 color : COLOR0; float seed : TEXCOORD1;
    float3 pixelInfo : TEXCOORD2; };
struct PixelInput { float4 position : SV_POSITION; float2 uv : TEXCOORD0;
    float4 color : COLOR0; float seed : TEXCOORD1;
    float3 pixelInfo : TEXCOORD2; };
PixelInput VSMain(VertexInput input) { PixelInput output;
    output.position=float4(input.position,0,1); output.uv=input.uv;
    output.color=input.color; output.seed=input.seed;
    output.pixelInfo=input.pixelInfo; return output; }
float Hash(float2 value) { return frac(sin(dot(value,float2(12.9898,78.233)))*43758.5453); }
float Noise(float2 value) { float2 cell=floor(value),f=frac(value);
    f=f*f*(3-2*f); float a=Hash(cell),b=Hash(cell+float2(1,0));
    float c=Hash(cell+float2(0,1)),d=Hash(cell+1);
    return lerp(lerp(a,b,f.x),lerp(c,d,f.x),f.y); }
float4 PSMain(PixelInput input) : SV_TARGET { float2 p=input.uv*2-1;
    if (input.seed<-4.5) { float radius=length(p);
        float angle=atan2(p.y,p.x); float spoke=pow(abs(cos(angle*7)),36);
        float inner=smoothstep(.16,.2,radius);
        float outer=1-smoothstep(.96,1,radius);
        float alpha=spoke*inner*outer*input.color.a;
        return float4(input.color.rgb*alpha,alpha); }
    if (input.seed<-3.5) { float radial=saturate(1-length(p));
        float alpha=pow(radial,.55)*input.color.a;
        return float4(input.color.rgb*alpha,alpha); }
    if (input.seed<-2.5) { float radius=length(p);
        float ring=exp(-pow((radius-.76)/.055,2));
        float alpha=ring*input.color.a;
        return float4(input.color.rgb*alpha,alpha); }
    if (input.seed<0) { float radial=saturate(1-length(p));
        float shape=input.seed<-1.5 ? radial*radial : pow(radial,.65);
        float alpha=shape*input.color.a;
        return float4(input.color.rgb*alpha,alpha); }
    // Vanilla FireSmoke quantizes screen position first. _spriteRect is a
    // camera/level-space mapping, not a per-particle rectangle, so the
    // dominant turbulence should never be reconstructed from the rotated quad.
    float pixelCell=max(input.pixelInfo.x,.001);
    float2 screenPos=input.position.xy+input.pixelInfo.yz;
    float2 snappedPos=floor(screenPos/pixelCell)*pixelCell;
    float2 virtualScreen=float2(1366.0,768.0)*pixelCell;
    float2 textCoord=snappedPos/virtualScreen;
    textCoord.y+=.04;

    // Keep the radial body in local sprite UVs so the smoke stays circular,
    // while the major noise layers remain screen-axis aligned like vanilla.
    float dist=saturate(1-length(p));
    const float rain=.5; const float tau=6.28318530718;
    // SpriteRenderer adds (Lifetime % 97) * .113 to the smoke seed.
    // Remove that integer lifetime component here so one particle keeps the
    // same procedural identity for its entire life.
    float stableSeed=frac(input.seed/.113);
    stableSeed=floor(stableSeed*1024+.5)/1024;
    float h=sin((1.77*rain+Noise(float2(
        textCoord.x*5.2+stableSeed*7,
        rain*.1+textCoord.y*2.6+stableSeed*13))*3)*tau)*.5+.5;
    h*=sin((3.5*rain+Noise(float2(
        textCoord.x*12.2+stableSeed*19,
        rain*.25+textCoord.y*6.6+stableSeed*3))*3)*tau)*.5+.5;
    // Vanilla keeps one local-UV noise layer, so rotation still adds a small
    // amount of organic variation without rotating the entire smoke mass.
    h*=.5+.5*sin((Noise(input.uv+stableSeed*float2(11,17))+
        rain)*tau*3);
    h=lerp(h*dist,lerp(h,1,lerp(.3,.8,input.color.a)),dist);
    h-=Noise(float2(textCoord.x*15.2+stableSeed*5,
        rain*.1+textCoord.y*7.6+stableSeed*23))*
        lerp(.7,.3,input.color.a);
    float cutoff=h*input.color.a;
    clip(cutoff-.35);
    // Vanilla FireSmoke uses color alpha to decide which pixels survive, but
    // surviving pixels are emitted at alpha 1 instead of softly fading out.
    return float4(input.color.rgb,1); }
)";

    struct Renderer
    {
        HWND Window = nullptr;
        ComPtr<ID3D11Device> Device;
        ComPtr<ID3D11DeviceContext> Context;
        ComPtr<ID3D11DeviceContext1> Context1;
        ComPtr<IDCompositionDevice> CompositionDevice;
        ComPtr<IDCompositionTarget> Target;
        ComPtr<IDCompositionVisual> Root, BaseRoot, EffectRoot;
        std::array<Surface, MaximumSurfaces> Surfaces, EffectSurfaces;
        ComPtr<ID3D11VertexShader> EffectVertexShader;
        ComPtr<ID3D11PixelShader> EffectPixelShader;
        ComPtr<ID3D11InputLayout> EffectInputLayout;
        ComPtr<ID3D11Buffer> EffectVertexBuffer;
        ComPtr<ID3D11BlendState> EffectBlendState;
        HRESULT EffectStatus = E_FAIL;

        HRESULT Initialize(HWND window)
        {
            Window=window;
            UINT flags = D3D11_CREATE_DEVICE_BGRA_SUPPORT;
#if defined(_DEBUG)
            flags |= D3D11_CREATE_DEVICE_DEBUG;
#endif
            D3D_FEATURE_LEVEL level;
            HRESULT hr = D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,
                flags,nullptr,0,D3D11_SDK_VERSION,&Device,&level,&Context);
            if (FAILED(hr)) hr = D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_WARP,
                nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,nullptr,0,D3D11_SDK_VERSION,
                &Device,&level,&Context);
            if (FAILED(hr)) return hr;
            Context.As(&Context1);
            ComPtr<IDXGIDevice> dxgi;
            if (FAILED(hr=Device.As(&dxgi))) return hr;
            if (FAILED(hr=DCompositionCreateDevice(dxgi.Get(),IID_PPV_ARGS(&CompositionDevice)))) return hr;
            if (FAILED(hr=CompositionDevice->CreateTargetForHwnd(window,TRUE,&Target))) return hr;
            if (FAILED(hr=CompositionDevice->CreateVisual(&Root))) return hr;
            if (FAILED(hr=CompositionDevice->CreateVisual(&BaseRoot))) return hr;
            if (FAILED(hr=CompositionDevice->CreateVisual(&EffectRoot))) return hr;
            if (FAILED(hr=Root->AddVisual(BaseRoot.Get(),FALSE,nullptr))) return hr;
            if (FAILED(hr=Root->AddVisual(EffectRoot.Get(),TRUE,nullptr))) return hr;
            if (FAILED(hr=Target->SetRoot(Root.Get()))) return hr;
            EffectStatus = InitializeEffects();
            return CompositionDevice->Commit();
        }

        HRESULT InitializeEffects()
        {
            ComPtr<ID3DBlob> vs, ps, errors;
            HRESULT hr = D3DCompile(EffectShader,sizeof(EffectShader)-1,nullptr,nullptr,
                nullptr,"VSMain","vs_4_0",D3DCOMPILE_OPTIMIZATION_LEVEL3,0,&vs,&errors);
            if (FAILED(hr)) return hr;
            errors.Reset();
            hr = D3DCompile(EffectShader,sizeof(EffectShader)-1,nullptr,nullptr,nullptr,
                "PSMain","ps_4_0",D3DCOMPILE_OPTIMIZATION_LEVEL3,0,&ps,&errors);
            if (FAILED(hr)) return hr;
            if (FAILED(hr=Device->CreateVertexShader(vs->GetBufferPointer(),vs->GetBufferSize(),
                nullptr,&EffectVertexShader))) return hr;
            if (FAILED(hr=Device->CreatePixelShader(ps->GetBufferPointer(),ps->GetBufferSize(),
                nullptr,&EffectPixelShader))) return hr;
            D3D11_INPUT_ELEMENT_DESC elements[] = {
                {"POSITION",0,DXGI_FORMAT_R32G32_FLOAT,0,0,D3D11_INPUT_PER_VERTEX_DATA,0},
                {"TEXCOORD",0,DXGI_FORMAT_R32G32_FLOAT,0,8,D3D11_INPUT_PER_VERTEX_DATA,0},
                {"COLOR",0,DXGI_FORMAT_R32G32B32A32_FLOAT,0,16,D3D11_INPUT_PER_VERTEX_DATA,0},
                {"TEXCOORD",1,DXGI_FORMAT_R32_FLOAT,0,32,D3D11_INPUT_PER_VERTEX_DATA,0},
                {"TEXCOORD",2,DXGI_FORMAT_R32G32B32_FLOAT,0,36,D3D11_INPUT_PER_VERTEX_DATA,0} };
            if (FAILED(hr=Device->CreateInputLayout(elements,ARRAYSIZE(elements),
                vs->GetBufferPointer(),vs->GetBufferSize(),&EffectInputLayout))) return hr;
            D3D11_BUFFER_DESC buffer = {};
            buffer.ByteWidth=MaximumSmokeEffects*12*sizeof(EffectVertex);
            buffer.Usage=D3D11_USAGE_DYNAMIC; buffer.BindFlags=D3D11_BIND_VERTEX_BUFFER;
            buffer.CPUAccessFlags=D3D11_CPU_ACCESS_WRITE;
            if (FAILED(hr=Device->CreateBuffer(&buffer,nullptr,&EffectVertexBuffer))) return hr;
            D3D11_BLEND_DESC blend = {};
            auto& target=blend.RenderTarget[0]; target.BlendEnable=TRUE;
            target.SrcBlend=D3D11_BLEND_ONE; target.DestBlend=D3D11_BLEND_INV_SRC_ALPHA;
            target.BlendOp=D3D11_BLEND_OP_ADD; target.SrcBlendAlpha=D3D11_BLEND_ONE;
            target.DestBlendAlpha=D3D11_BLEND_INV_SRC_ALPHA;
            target.BlendOpAlpha=D3D11_BLEND_OP_ADD;
            target.RenderTargetWriteMask=D3D11_COLOR_WRITE_ENABLE_ALL;
            return Device->CreateBlendState(&blend,&EffectBlendState);
        }

        HRESULT EnsureSurface(std::array<Surface,MaximumSurfaces>& list,
            IDCompositionVisual* parent,int slot,UINT width,UINT height)
        {
            if (slot<0 || slot>=MaximumSurfaces || !width || !height) return E_INVALIDARG;
            Surface& s=list[slot];
            if (s.CompositionSurface && s.Width==width && s.Height==height) return S_OK;
            if (s.Visual) {
                HRESULT remove=parent->RemoveVisual(s.Visual.Get());
                if (FAILED(remove)) return remove;
                s.Visual.Reset(); s.CompositionSurface.Reset();
                s.Width=0; s.Height=0; s.Active=false; s.InactiveSince=0;
            }
            HRESULT hr=CompositionDevice->CreateSurface(width,height,
                DXGI_FORMAT_B8G8R8A8_UNORM,DXGI_ALPHA_MODE_PREMULTIPLIED,
                &s.CompositionSurface);
            if (FAILED(hr)) return hr;
            if (FAILED(hr=CompositionDevice->CreateVisual(&s.Visual))) return hr;
            if (FAILED(hr=s.Visual->SetContent(s.CompositionSurface.Get()))) return hr;
            if (FAILED(hr=parent->AddVisual(s.Visual.Get(),TRUE,nullptr))) return hr;
            s.Width=width; s.Height=height; s.Active=true; s.InactiveSince=0; return S_OK;
        }

        static HRESULT SetVisual(Surface& s,float x,float y)
        {
            HRESULT hr=s.Visual->SetOffsetX(x); if (FAILED(hr)) return hr;
            if (FAILED(hr=s.Visual->SetOffsetY(y))) return hr;
            if (FAILED(hr=s.Visual->SetContent(s.CompositionSurface.Get()))) return hr;
            s.Active=true; s.InactiveSince=0; return S_OK;
        }

        HRESULT Present(int slot,const void* pixels,UINT width,UINT height,
            UINT stride,float x,float y)
        {
            if (!pixels || stride<width*4) return E_INVALIDARG;
            HRESULT hr=EnsureSurface(Surfaces,BaseRoot.Get(),slot,width,height);
            if (FAILED(hr)) return hr;
            Surface& s=Surfaces[slot]; RECT rect={0,0,(LONG)width,(LONG)height};
            POINT offset={}; ComPtr<IDXGISurface> drawing;
            if (FAILED(hr=s.CompositionSurface->BeginDraw(&rect,IID_PPV_ARGS(&drawing),&offset))) return hr;
            ComPtr<ID3D11Texture2D> texture; HRESULT upload=drawing.As(&texture);
            if (SUCCEEDED(upload)) { D3D11_BOX box={(UINT)offset.x,(UINT)offset.y,0,
                (UINT)offset.x+width,(UINT)offset.y+height,1};
                Context->UpdateSubresource(texture.Get(),0,&box,pixels,stride,0); }
            HRESULT end=s.CompositionSurface->EndDraw();
            if (FAILED(upload)) return upload; if (FAILED(end)) return end;
            return SetVisual(s,x,y);
        }

        static void AddQuad(std::vector<EffectVertex>& out,float cx,float cy,
            float size,float rotation,float r,float g,float b,float a,float seed,
            float pixelScale,float screenBiasX,float screenBiasY,
            UINT width,UINT height)
        {
            if (size<=.01f || a<=.001f) return;
            float rad=rotation*.01745329252f,c=std::cos(rad),s=std::sin(rad),h=size*.5f;
            const float local[4][2]={{-h,-h},{h,-h},{h,h},{-h,h}};
            const float uv[4][2]={{0,0},{1,0},{1,1},{0,1}};
            EffectVertex v[4]={};
            for (int i=0;i<4;++i) { float px=cx+local[i][0]*c-local[i][1]*s;
                float py=cy+local[i][0]*s+local[i][1]*c;
                v[i]={px/width*2-1,1-py/height*2,uv[i][0],uv[i][1],r,g,b,a,
                    seed,pixelScale,screenBiasX,screenBiasY}; }
            const int indices[6]={0,1,2,0,2,3};
            for (int i:indices) out.push_back(v[i]);
        }

        HRESULT PresentEffects(int slot,const GpuSmokeEffect* effects,UINT count,
            UINT width,UINT height,float x,float y)
        {
            if (FAILED(EffectStatus)) return EffectStatus;
            if (!effects || !count || count>MaximumSmokeEffects) return E_INVALIDARG;
            HRESULT hr=EnsureSurface(EffectSurfaces,EffectRoot.Get(),slot,width,height);
            if (FAILED(hr)) return hr;
            Surface& s=EffectSurfaces[slot]; RECT rect={0,0,(LONG)width,(LONG)height};
            POINT offset={}; ComPtr<IDXGISurface> drawing;
            if (FAILED(hr=s.CompositionSurface->BeginDraw(&rect,IID_PPV_ARGS(&drawing),&offset))) return hr;
            HRESULT draw=S_OK; ComPtr<ID3D11Texture2D> texture;
            ComPtr<ID3D11RenderTargetView> target;
            if (SUCCEEDED(draw=drawing.As(&texture)))
                draw=Device->CreateRenderTargetView(texture.Get(),nullptr,&target);

            // Rain World's 16:9 render grid is 1366x768. Use the smaller
            // client-axis scale so virtual pixels stay square on widescreen
            // and multi-monitor desktop windows.
            float pixelScale=1.0f;
            RECT client={};
            if (Window && GetClientRect(Window,&client)) {
                float clientWidth=(float)(client.right-client.left);
                float clientHeight=(float)(client.bottom-client.top);
                if (clientWidth>0 && clientHeight>0) {
                    float scaleX=clientWidth/1366.0f;
                    float scaleY=clientHeight/768.0f;
                    pixelScale=scaleX<scaleY?scaleX:scaleY;
                    if (pixelScale<.001f) pixelScale=.001f;
                }
            }
            float screenBiasX=x-(float)offset.x;
            float screenBiasY=y-(float)offset.y;

            std::vector<EffectVertex> vertices;
            if (SUCCEEDED(draw)) { vertices.reserve(count*12);
                for (UINT i=0;i<count;++i) { const auto& e=effects[i];
                    AddQuad(vertices,e.CenterX,e.CenterY,e.BackSize,e.Rotation,e.BackRed,
                        e.BackGreen,e.BackBlue,e.BackAlpha,e.Seed,pixelScale,
                        screenBiasX,screenBiasY,width,height);
                    AddQuad(vertices,e.CenterX,e.CenterY,e.FrontSize,e.Rotation,e.FrontRed,
                        e.FrontGreen,e.FrontBlue,e.FrontAlpha,
                        e.Seed==-1?-2:e.Seed+.37f,pixelScale,
                        screenBiasX,screenBiasY,width,height); }
                D3D11_MAPPED_SUBRESOURCE mapped={};
                draw=Context->Map(EffectVertexBuffer.Get(),0,D3D11_MAP_WRITE_DISCARD,0,&mapped);
                if (SUCCEEDED(draw)) { std::memcpy(mapped.pData,vertices.data(),
                    vertices.size()*sizeof(EffectVertex)); Context->Unmap(EffectVertexBuffer.Get(),0); } }
            if (SUCCEEDED(draw)) { const float clear[4]={0,0,0,0};
                if (Context1) { D3D11_RECT area={offset.x,offset.y,offset.x+(LONG)width,
                    offset.y+(LONG)height}; Context1->ClearView(target.Get(),clear,&area,1); }
                else Context->ClearRenderTargetView(target.Get(),clear);
                D3D11_VIEWPORT vp={(float)offset.x,(float)offset.y,(float)width,(float)height,0,1};
                UINT stride=sizeof(EffectVertex),zero=0; const float factor[4]={0,0,0,0};
                Context->OMSetRenderTargets(1,target.GetAddressOf(),nullptr);
                Context->OMSetBlendState(EffectBlendState.Get(),factor,0xffffffff);
                Context->RSSetViewports(1,&vp); Context->IASetInputLayout(EffectInputLayout.Get());
                Context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
                Context->IASetVertexBuffers(0,1,EffectVertexBuffer.GetAddressOf(),&stride,&zero);
                Context->VSSetShader(EffectVertexShader.Get(),nullptr,0);
                Context->PSSetShader(EffectPixelShader.Get(),nullptr,0);
                Context->Draw((UINT)vertices.size(),0); ID3D11RenderTargetView* none=nullptr;
                Context->OMSetRenderTargets(1,&none,nullptr); }
            HRESULT end=s.CompositionSurface->EndDraw();
            if (FAILED(draw)) return draw; if (FAILED(end)) return end;
            return SetVisual(s,x,y);
        }

        static HRESULT ApplyMask(std::array<Surface,MaximumSurfaces>& list,
            IDCompositionVisual* parent,UINT mask,ULONGLONG now)
        {
            for (int i=0;i<MaximumSurfaces;++i) {
                Surface& s=list[i];
                bool active=(mask&(1u<<i))!=0;
                if (active) {
                    s.Active=true; s.InactiveSince=0; continue;
                }
                if (s.Visual && s.Active) {
                    HRESULT hr=s.Visual->SetContent(nullptr); if (FAILED(hr)) return hr;
                }
                s.Active=false;
                if (!s.Visual) { s.InactiveSince=0; continue; }
                if (!s.InactiveSince) { s.InactiveSince=now; continue; }
                if (now-s.InactiveSince<InactiveSurfaceReleaseMilliseconds) continue;
                HRESULT hr=parent->RemoveVisual(s.Visual.Get()); if (FAILED(hr)) return hr;
                s.Visual.Reset(); s.CompositionSurface.Reset();
                s.Width=0; s.Height=0; s.InactiveSince=0;
            }
            return S_OK;
        }

        HRESULT Commit(UINT activeMask,UINT effectMask)
        {
            ULONGLONG now=GetTickCount64();
            HRESULT hr=ApplyMask(Surfaces,BaseRoot.Get(),activeMask,now); if (FAILED(hr)) return hr;
            if (FAILED(hr=ApplyMask(EffectSurfaces,EffectRoot.Get(),effectMask,now))) return hr;
            return CompositionDevice->Commit();
        }
    };
}

extern "C" __declspec(dllexport) HRESULT __stdcall SlugcatDCompCreate(HWND window,Renderer** renderer)
{ if (!window||!renderer) return E_INVALIDARG; *renderer=nullptr;
  std::unique_ptr<Renderer> value(new(std::nothrow) Renderer()); if(!value)return E_OUTOFMEMORY;
  HRESULT hr=value->Initialize(window); if(FAILED(hr))return hr; *renderer=value.release(); return S_OK; }
extern "C" __declspec(dllexport) HRESULT __stdcall SlugcatDCompPresent(Renderer* r,int slot,
    const void* pixels,UINT width,UINT height,UINT stride,float x,float y)
{ return r?r->Present(slot,pixels,width,height,stride,x,y):E_POINTER; }
extern "C" __declspec(dllexport) HRESULT __stdcall SlugcatDCompPresentEffects(Renderer* r,
    int slot,const GpuSmokeEffect* effects,UINT count,UINT width,UINT height,float x,float y)
{ return r?r->PresentEffects(slot,effects,count,width,height,x,y):E_POINTER; }
extern "C" __declspec(dllexport) HRESULT __stdcall SlugcatDCompCommit(Renderer* r,
    UINT activeMask,UINT effectMask)
{ return r?r->Commit(activeMask,effectMask):E_POINTER; }
extern "C" __declspec(dllexport) void __stdcall SlugcatDCompDestroy(Renderer* renderer)
{ delete renderer; }