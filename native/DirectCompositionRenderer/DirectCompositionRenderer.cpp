#include <windows.h>
#include <d3d11.h>
#include <dcomp.h>
#include <dxgi.h>
#include <wrl/client.h>
#include <array>
#include <memory>

using Microsoft::WRL::ComPtr;

namespace
{
    constexpr int MaximumSurfaces = 8;

    struct Surface
    {
        ComPtr<IDCompositionSurface> CompositionSurface;
        ComPtr<IDCompositionVisual> Visual;
        UINT Width = 0;
        UINT Height = 0;
        bool Active = false;
    };

    struct Renderer
    {
        ComPtr<ID3D11Device> Device;
        ComPtr<ID3D11DeviceContext> Context;
        ComPtr<IDCompositionDevice> CompositionDevice;
        ComPtr<IDCompositionTarget> Target;
        ComPtr<IDCompositionVisual> Root;
        std::array<Surface, MaximumSurfaces> Surfaces;

        HRESULT Initialize(HWND window)
        {
            UINT flags = D3D11_CREATE_DEVICE_BGRA_SUPPORT;
#if defined(_DEBUG)
            flags |= D3D11_CREATE_DEVICE_DEBUG;
#endif
            D3D_FEATURE_LEVEL featureLevel;
            HRESULT result = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr,
                flags, nullptr, 0, D3D11_SDK_VERSION, &Device, &featureLevel, &Context);
            if (FAILED(result))
            {
                result = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_WARP, nullptr,
                    D3D11_CREATE_DEVICE_BGRA_SUPPORT, nullptr, 0, D3D11_SDK_VERSION,
                    &Device, &featureLevel, &Context);
            }
            if (FAILED(result)) return result;

            ComPtr<IDXGIDevice> dxgiDevice;
            result = Device.As(&dxgiDevice);
            if (FAILED(result)) return result;
            result = DCompositionCreateDevice(dxgiDevice.Get(), IID_PPV_ARGS(&CompositionDevice));
            if (FAILED(result)) return result;
            result = CompositionDevice->CreateTargetForHwnd(window, TRUE, &Target);
            if (FAILED(result)) return result;
            result = CompositionDevice->CreateVisual(&Root);
            if (FAILED(result)) return result;
            result = Target->SetRoot(Root.Get());
            if (FAILED(result)) return result;
            return CompositionDevice->Commit();
        }

        HRESULT EnsureSurface(int slot, UINT width, UINT height)
        {
            if (slot < 0 || slot >= MaximumSurfaces || width == 0 || height == 0)
                return E_INVALIDARG;
            Surface& surface = Surfaces[slot];
            if (surface.CompositionSurface && surface.Width == width && surface.Height == height)
                return S_OK;

            if (surface.Visual)
            {
                Root->RemoveVisual(surface.Visual.Get());
                surface.Visual.Reset();
                surface.CompositionSurface.Reset();
            }

            HRESULT result = CompositionDevice->CreateSurface(width, height,
                DXGI_FORMAT_B8G8R8A8_UNORM, DXGI_ALPHA_MODE_PREMULTIPLIED,
                &surface.CompositionSurface);
            if (FAILED(result)) return result;
            result = CompositionDevice->CreateVisual(&surface.Visual);
            if (FAILED(result)) return result;
            result = surface.Visual->SetContent(surface.CompositionSurface.Get());
            if (FAILED(result)) return result;
            result = Root->AddVisual(surface.Visual.Get(), TRUE, nullptr);
            if (FAILED(result)) return result;
            surface.Width = width;
            surface.Height = height;
            surface.Active = true;
            return S_OK;
        }

        HRESULT Present(int slot, const void* pixels, UINT width, UINT height,
            UINT stride, float x, float y)
        {
            if (!pixels || stride < width * 4) return E_INVALIDARG;
            HRESULT result = EnsureSurface(slot, width, height);
            if (FAILED(result)) return result;
            Surface& surface = Surfaces[slot];
            RECT updateRectangle = { 0, 0, static_cast<LONG>(width), static_cast<LONG>(height) };
            POINT updateOffset = {};
            ComPtr<IDXGISurface> drawingSurface;
            result = surface.CompositionSurface->BeginDraw(&updateRectangle,
                IID_PPV_ARGS(&drawingSurface), &updateOffset);
            if (FAILED(result)) return result;
            ComPtr<ID3D11Texture2D> texture;
            result = drawingSurface.As(&texture);
            HRESULT uploadResult = result;
            if (SUCCEEDED(result))
            {
                D3D11_BOX destination = {};
                destination.left = static_cast<UINT>(updateOffset.x);
                destination.top = static_cast<UINT>(updateOffset.y);
                destination.right = destination.left + width;
                destination.bottom = destination.top + height;
                destination.front = 0;
                destination.back = 1;
                Context->UpdateSubresource(texture.Get(), 0, &destination, pixels, stride, 0);
            }
            HRESULT endDrawResult = surface.CompositionSurface->EndDraw();
            if (FAILED(uploadResult)) return uploadResult;
            if (FAILED(endDrawResult)) return endDrawResult;
            result = surface.Visual->SetOffsetX(x);
            if (FAILED(result)) return result;
            result = surface.Visual->SetOffsetY(y);
            if (FAILED(result)) return result;
            result = surface.Visual->SetContent(surface.CompositionSurface.Get());
            if (FAILED(result)) return result;
            surface.Active = true;
            return S_OK;
        }

        HRESULT Commit(UINT activeMask)
        {
            for (int index = 0; index < MaximumSurfaces; ++index)
            {
                Surface& surface = Surfaces[index];
                bool active = (activeMask & (1u << index)) != 0;
                if (surface.Visual && !active && surface.Active)
                {
                    HRESULT result = surface.Visual->SetContent(nullptr);
                    if (FAILED(result)) return result;
                }
                surface.Active = active;
            }
            return CompositionDevice->Commit();
        }
    };
}

extern "C" __declspec(dllexport) HRESULT __stdcall SlugcatDCompCreate(
    HWND window, Renderer** renderer)
{
    if (!window || !renderer) return E_INVALIDARG;
    *renderer = nullptr;
    std::unique_ptr<Renderer> value(new (std::nothrow) Renderer());
    if (!value) return E_OUTOFMEMORY;
    HRESULT result = value->Initialize(window);
    if (FAILED(result)) return result;
    *renderer = value.release();
    return S_OK;
}

extern "C" __declspec(dllexport) HRESULT __stdcall SlugcatDCompPresent(
    Renderer* renderer, int slot, const void* pixels, UINT width, UINT height,
    UINT stride, float x, float y)
{
    return renderer ? renderer->Present(slot, pixels, width, height, stride, x, y) : E_POINTER;
}

extern "C" __declspec(dllexport) HRESULT __stdcall SlugcatDCompCommit(
    Renderer* renderer, UINT activeMask)
{
    return renderer ? renderer->Commit(activeMask) : E_POINTER;
}

extern "C" __declspec(dllexport) void __stdcall SlugcatDCompDestroy(Renderer* renderer)
{
    delete renderer;
}
