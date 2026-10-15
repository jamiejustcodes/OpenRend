#include "openrend.h"
#include <Windows.h>
#include <d3d11.h>
#include <d3dcompiler.h>
#include <wrl/client.h>
#include <algorithm>
#include <memory>
#include <mutex>
#include <stdexcept>
#include <string>
#include <cstring>

using Microsoft::WRL::ComPtr;
void or_set_error(const char* message);
namespace {
void check(HRESULT result, const char* operation) { if (FAILED(result)) { char code[24]{}; sprintf_s(code, " (0x%08lX)", static_cast<unsigned long>(result)); throw std::runtime_error(std::string(operation) + code); } }
void require(bool condition, const char* message) { if (!condition) throw std::runtime_error(message); }
template<class F> int safe(F&& operation) { try { or_set_error(""); return operation(); } catch (const std::exception& e) { or_set_error(e.what()); return -1; } catch (...) { or_set_error("Unexpected D3D11 renderer failure"); return -1; } }
struct Renderer {
    std::mutex mutex;
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;
    ComPtr<IDXGISwapChain> swapchain;
    ComPtr<ID3D11RenderTargetView> target;
    ComPtr<ID3D11Texture2D> texture;
    ComPtr<ID3D11ShaderResourceView> source;
    ComPtr<ID3D11VertexShader> vertex;
    ComPtr<ID3D11PixelShader> pixel;
    ComPtr<ID3D11SamplerState> sampler;
    ComPtr<ID3D11RasterizerState> rasterizer;
    int width{}, height{}, texture_width{}, texture_height{};
    bool hardware{};
    void create_target() { ComPtr<ID3D11Texture2D> buffer; check(swapchain->GetBuffer(0, IID_PPV_ARGS(&buffer)), "Get preview swapchain buffer"); check(device->CreateRenderTargetView(buffer.Get(), nullptr, &target), "Create preview render target"); }
    void resize(int w, int h) {
        require(w > 0 && h > 0, "Invalid preview dimensions"); if (w == width && h == height) return;
        context->OMSetRenderTargets(0, nullptr, nullptr); target.Reset(); check(swapchain->ResizeBuffers(0, w, h, DXGI_FORMAT_UNKNOWN, 0), "Resize preview"); width = w; height = h; create_target(); draw();
    }
    void draw() {
        const float black[] = { 0.015f, 0.018f, 0.025f, 1 }; context->ClearRenderTargetView(target.Get(), black);
        if (source && texture_width > 0 && texture_height > 0) {
            float scale = std::min(static_cast<float>(width) / texture_width, static_cast<float>(height) / texture_height);
            D3D11_VIEWPORT viewport{}; viewport.Width = texture_width * scale; viewport.Height = texture_height * scale; viewport.TopLeftX = (width - viewport.Width) / 2; viewport.TopLeftY = (height - viewport.Height) / 2; viewport.MaxDepth = 1;
            context->RSSetViewports(1, &viewport); context->RSSetState(rasterizer.Get()); ID3D11RenderTargetView* output = target.Get(); context->OMSetRenderTargets(1, &output, nullptr); context->IASetInputLayout(nullptr); context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST); context->VSSetShader(vertex.Get(), nullptr, 0); context->PSSetShader(pixel.Get(), nullptr, 0);
            ID3D11ShaderResourceView* input = source.Get(); context->PSSetShaderResources(0, 1, &input); ID3D11SamplerState* sample = sampler.Get(); context->PSSetSamplers(0, 1, &sample); context->Draw(3, 0);
        }
        check(swapchain->Present(0, 0), "Present preview frame");
    }
};
constexpr char shader[] = R"(
Texture2D image : register(t0);
SamplerState linearSampler : register(s0);
struct Vertex { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
Vertex vs(uint id : SV_VertexID) {
    Vertex o; o.uv = float2((id << 1) & 2, id & 2);
    o.position = float4(o.uv * float2(2,-2) + float2(-1,1),0,1); return o;
}
float4 ps(Vertex i) : SV_TARGET { return image.Sample(linearSampler, i.uv); }
)";
}
extern "C" {
int or_renderer_create(void* hwnd, int width, int height, void** output) { return safe([&] {
    require(hwnd && output && IsWindow(static_cast<HWND>(hwnd)) && width > 0 && height > 0, "Invalid preview window arguments"); *output = nullptr;
    auto r = std::make_unique<Renderer>(); r->width = width; r->height = height;
    DXGI_SWAP_CHAIN_DESC desc{}; desc.BufferDesc.Width = width; desc.BufferDesc.Height = height; desc.BufferDesc.Format = DXGI_FORMAT_B8G8R8A8_UNORM; desc.SampleDesc.Count = 1; desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT; desc.BufferCount = 2; desc.OutputWindow = static_cast<HWND>(hwnd); desc.Windowed = TRUE; desc.SwapEffect = DXGI_SWAP_EFFECT_DISCARD;
    D3D_FEATURE_LEVEL level{};
    auto result = D3D11CreateDeviceAndSwapChain(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, D3D11_CREATE_DEVICE_BGRA_SUPPORT, nullptr, 0, D3D11_SDK_VERSION, &desc, &r->swapchain, &r->device, &level, &r->context);
    r->hardware = SUCCEEDED(result);
    if (FAILED(result)) { r->swapchain.Reset(); r->device.Reset(); r->context.Reset(); result = D3D11CreateDeviceAndSwapChain(nullptr, D3D_DRIVER_TYPE_WARP, nullptr, D3D11_CREATE_DEVICE_BGRA_SUPPORT, nullptr, 0, D3D11_SDK_VERSION, &desc, &r->swapchain, &r->device, &level, &r->context); }
    check(result, "Create D3D11 preview device"); r->create_target();
    ComPtr<ID3DBlob> vs, ps, errors;
    check(D3DCompile(shader, sizeof(shader), "OpenRendPreview", nullptr, nullptr, "vs", "vs_4_0", D3DCOMPILE_OPTIMIZATION_LEVEL3, 0, &vs, &errors), "Compile preview vertex shader");
    check(D3DCompile(shader, sizeof(shader), "OpenRendPreview", nullptr, nullptr, "ps", "ps_4_0", D3DCOMPILE_OPTIMIZATION_LEVEL3, 0, &ps, &errors), "Compile preview pixel shader");
    check(r->device->CreateVertexShader(vs->GetBufferPointer(), vs->GetBufferSize(), nullptr, &r->vertex), "Create preview vertex shader");
    check(r->device->CreatePixelShader(ps->GetBufferPointer(), ps->GetBufferSize(), nullptr, &r->pixel), "Create preview pixel shader");
    D3D11_SAMPLER_DESC sampler{}; sampler.Filter = D3D11_FILTER_MIN_MAG_MIP_LINEAR; sampler.AddressU = sampler.AddressV = sampler.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP; sampler.MaxLOD = D3D11_FLOAT32_MAX;
    check(r->device->CreateSamplerState(&sampler, &r->sampler), "Create preview sampler");
    D3D11_RASTERIZER_DESC rasterizer{}; rasterizer.FillMode = D3D11_FILL_SOLID; rasterizer.CullMode = D3D11_CULL_NONE; rasterizer.DepthClipEnable = TRUE;
    check(r->device->CreateRasterizerState(&rasterizer, &r->rasterizer), "Create preview rasterizer"); r->draw(); *output = r.release(); return 0;
}); }
int or_renderer_present(void* handle, const uint8_t* bgra, int width, int height, int stride) { return safe([&] {
    require(handle && bgra && width > 0 && height > 0 && int64_t(stride) >= int64_t(width) * 4, "Invalid preview frame arguments"); auto& r = *static_cast<Renderer*>(handle); std::lock_guard lock(r.mutex);
    if (r.texture_width != width || r.texture_height != height) {
        ID3D11ShaderResourceView* empty{}; r.context->PSSetShaderResources(0, 1, &empty); r.source.Reset(); r.texture.Reset();
        D3D11_TEXTURE2D_DESC desc{}; desc.Width = width; desc.Height = height; desc.MipLevels = 1; desc.ArraySize = 1; desc.Format = DXGI_FORMAT_B8G8R8A8_UNORM; desc.SampleDesc.Count = 1; desc.Usage = D3D11_USAGE_DYNAMIC; desc.BindFlags = D3D11_BIND_SHADER_RESOURCE; desc.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
        check(r.device->CreateTexture2D(&desc, nullptr, &r.texture), "Create preview frame texture"); check(r.device->CreateShaderResourceView(r.texture.Get(), nullptr, &r.source), "Create preview texture view"); r.texture_width = width; r.texture_height = height;
    }
    D3D11_MAPPED_SUBRESOURCE mapped{}; check(r.context->Map(r.texture.Get(), 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped), "Upload preview frame");
    for (int y = 0; y < height; ++y) std::memcpy(static_cast<uint8_t*>(mapped.pData) + static_cast<size_t>(y) * mapped.RowPitch, bgra + static_cast<size_t>(y) * stride, static_cast<size_t>(width) * 4);
    r.context->Unmap(r.texture.Get(), 0);
    r.draw(); return 0;
}); }
int or_renderer_resize(void* handle, int width, int height) { return safe([&] { require(handle, "Missing preview renderer"); auto& r = *static_cast<Renderer*>(handle); std::lock_guard lock(r.mutex); r.resize(width, height); return 0; }); }
int or_renderer_is_hardware(void* handle) { if (!handle) return 0; return static_cast<Renderer*>(handle)->hardware ? 1 : 0; }
void or_renderer_close(void* handle) { delete static_cast<Renderer*>(handle); }
}
