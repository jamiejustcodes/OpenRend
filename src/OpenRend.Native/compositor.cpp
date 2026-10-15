#include "openrend.h"
#include <Windows.h>
#include <d3d11.h>
#include <d3dcompiler.h>
#include <wrl/client.h>
#include <algorithm>
#include <cmath>
#include <cstring>
#include <memory>
#include <mutex>
#include <stdexcept>
#include <string>

using Microsoft::WRL::ComPtr;
void or_set_error(const char* message);
namespace {
void check(HRESULT status, const char* operation) {
    if (FAILED(status)) { char code[24]{}; sprintf_s(code, " (0x%08lX)", static_cast<unsigned long>(status)); throw std::runtime_error(std::string(operation) + code); }
}
void require(bool condition, const char* message) { if (!condition) throw std::runtime_error(message); }
template<class F> int safe(F&& operation) {
    try { or_set_error(""); return operation(); }
    catch (const std::exception& e) { or_set_error(e.what()); return -1; }
    catch (...) { or_set_error("Unexpected D3D11 compositing failure"); return -1; }
}
constexpr char shader[] = R"(
Texture2D image : register(t0);
SamplerState linearSampler : register(s0);
cbuffer Layer : register(b0) { float opacity; float3 padding; };
struct Vertex { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
Vertex vs(uint id : SV_VertexID) {
    Vertex o; o.uv = float2((id << 1) & 2, id & 2);
    o.position = float4(o.uv * float2(2,-2) + float2(-1,1),0,1); return o;
}
float4 ps(Vertex i) : SV_TARGET {
    float4 color = image.Sample(linearSampler, i.uv);
    color.a *= opacity; return color;
}
)";
struct Compositor {
    std::mutex mutex;
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;
    ComPtr<ID3D11Texture2D> output, staging, layer;
    ComPtr<ID3D11RenderTargetView> target;
    ComPtr<ID3D11ShaderResourceView> source;
    ComPtr<ID3D11VertexShader> vertex;
    ComPtr<ID3D11PixelShader> pixel;
    ComPtr<ID3D11Buffer> constants;
    ComPtr<ID3D11BlendState> blend;
    ComPtr<ID3D11SamplerState> sampler;
    ComPtr<ID3D11RasterizerState> rasterizer;
    int width{}, height{}, layer_width{}, layer_height{};
    bool hardware{}, started{};
    void begin() { const float black[] = { 0, 0, 0, 1 }; context->ClearRenderTargetView(target.Get(), black); started = true; }
};
}
extern "C" {
int or_compositor_create(int width, int height, void** result) { return safe([&] {
    require(result != nullptr, "Missing compositor handle output"); *result = nullptr;
    require(width > 0 && height > 0 && width <= 16384 && height <= 16384 && int64_t(width) * height * 4 <= INT_MAX, "Invalid compositor dimensions");
    auto c = std::make_unique<Compositor>(); c->width = width; c->height = height;
    wchar_t force_warp[2]{}; const bool warp = GetEnvironmentVariableW(L"OPENREND_FORCE_WARP", force_warp, 2) == 1 && force_warp[0] == L'1';
    HRESULT status = E_FAIL;
    if (!warp) status = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, D3D11_CREATE_DEVICE_BGRA_SUPPORT, nullptr, 0, D3D11_SDK_VERSION, &c->device, nullptr, &c->context);
    c->hardware = SUCCEEDED(status);
    if (FAILED(status)) { c->device.Reset(); c->context.Reset(); status = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_WARP, nullptr, D3D11_CREATE_DEVICE_BGRA_SUPPORT, nullptr, 0, D3D11_SDK_VERSION, &c->device, nullptr, &c->context); }
    check(status, "Create compositing device");
    D3D11_TEXTURE2D_DESC texture{}; texture.Width = width; texture.Height = height; texture.MipLevels = 1; texture.ArraySize = 1; texture.Format = DXGI_FORMAT_B8G8R8A8_UNORM; texture.SampleDesc.Count = 1; texture.Usage = D3D11_USAGE_DEFAULT; texture.BindFlags = D3D11_BIND_RENDER_TARGET;
    check(c->device->CreateTexture2D(&texture, nullptr, &c->output), "Create compositing output texture");
    check(c->device->CreateRenderTargetView(c->output.Get(), nullptr, &c->target), "Create compositing output target");
    texture.Usage = D3D11_USAGE_STAGING; texture.BindFlags = 0; texture.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    check(c->device->CreateTexture2D(&texture, nullptr, &c->staging), "Create compositing readback texture");
    ComPtr<ID3DBlob> vs, ps, errors;
    check(D3DCompile(shader, sizeof(shader), "OpenRendCompositor", nullptr, nullptr, "vs", "vs_4_0", D3DCOMPILE_OPTIMIZATION_LEVEL3, 0, &vs, &errors), "Compile compositor vertex shader");
    check(D3DCompile(shader, sizeof(shader), "OpenRendCompositor", nullptr, nullptr, "ps", "ps_4_0", D3DCOMPILE_OPTIMIZATION_LEVEL3, 0, &ps, &errors), "Compile compositor pixel shader");
    check(c->device->CreateVertexShader(vs->GetBufferPointer(), vs->GetBufferSize(), nullptr, &c->vertex), "Create compositor vertex shader");
    check(c->device->CreatePixelShader(ps->GetBufferPointer(), ps->GetBufferSize(), nullptr, &c->pixel), "Create compositor pixel shader");
    D3D11_BUFFER_DESC buffer{}; buffer.ByteWidth = 16; buffer.Usage = D3D11_USAGE_DYNAMIC; buffer.BindFlags = D3D11_BIND_CONSTANT_BUFFER; buffer.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
    check(c->device->CreateBuffer(&buffer, nullptr, &c->constants), "Create compositor opacity buffer");
    D3D11_BLEND_DESC blend{}; auto& rt = blend.RenderTarget[0]; rt.BlendEnable = TRUE; rt.SrcBlend = D3D11_BLEND_SRC_ALPHA; rt.DestBlend = D3D11_BLEND_INV_SRC_ALPHA; rt.BlendOp = D3D11_BLEND_OP_ADD; rt.SrcBlendAlpha = D3D11_BLEND_ZERO; rt.DestBlendAlpha = D3D11_BLEND_ONE; rt.BlendOpAlpha = D3D11_BLEND_OP_ADD; rt.RenderTargetWriteMask = D3D11_COLOR_WRITE_ENABLE_ALL;
    check(c->device->CreateBlendState(&blend, &c->blend), "Create compositor alpha blend state");
    D3D11_SAMPLER_DESC sampler{}; sampler.Filter = D3D11_FILTER_MIN_MAG_MIP_LINEAR; sampler.AddressU = sampler.AddressV = sampler.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP; sampler.MaxLOD = D3D11_FLOAT32_MAX;
    check(c->device->CreateSamplerState(&sampler, &c->sampler), "Create compositor sampler");
    D3D11_RASTERIZER_DESC rasterizer{}; rasterizer.FillMode = D3D11_FILL_SOLID; rasterizer.CullMode = D3D11_CULL_NONE; rasterizer.DepthClipEnable = TRUE;
    check(c->device->CreateRasterizerState(&rasterizer, &c->rasterizer), "Create compositor rasterizer"); c->begin(); *result = c.release(); return 0;
}); }
int or_compositor_begin(void* handle) { return safe([&] { require(handle, "Missing compositor handle"); auto& c = *static_cast<Compositor*>(handle); std::lock_guard lock(c.mutex); c.begin(); return 0; }); }
int or_compositor_layer(void* handle, const uint8_t* bgra, int width, int height, int stride, double opacity) { return safe([&] {
    require(handle && bgra && width > 0 && height > 0 && width <= 16384 && height <= 16384 && int64_t(stride) >= int64_t(width) * 4 && std::isfinite(opacity) && opacity >= 0 && opacity <= 1, "Invalid compositor layer arguments");
    auto& c = *static_cast<Compositor*>(handle); std::lock_guard lock(c.mutex); require(c.started, "Begin the compositor frame before adding layers"); if (opacity == 0) return 0;
    if (width != c.layer_width || height != c.layer_height) {
        ID3D11ShaderResourceView* empty{}; c.context->PSSetShaderResources(0, 1, &empty); c.source.Reset(); c.layer.Reset();
        D3D11_TEXTURE2D_DESC texture{}; texture.Width = width; texture.Height = height; texture.MipLevels = 1; texture.ArraySize = 1; texture.Format = DXGI_FORMAT_B8G8R8A8_UNORM; texture.SampleDesc.Count = 1; texture.Usage = D3D11_USAGE_DYNAMIC; texture.BindFlags = D3D11_BIND_SHADER_RESOURCE; texture.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
        check(c.device->CreateTexture2D(&texture, nullptr, &c.layer), "Create compositor layer texture"); check(c.device->CreateShaderResourceView(c.layer.Get(), nullptr, &c.source), "Create compositor layer view"); c.layer_width = width; c.layer_height = height;
    }
    D3D11_MAPPED_SUBRESOURCE mapped{}; check(c.context->Map(c.layer.Get(), 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped), "Upload compositor layer");
    for (int y = 0; y < height; ++y) std::memcpy(static_cast<uint8_t*>(mapped.pData) + static_cast<size_t>(y) * mapped.RowPitch, bgra + static_cast<size_t>(y) * stride, static_cast<size_t>(width) * 4);
    c.context->Unmap(c.layer.Get(), 0);
    check(c.context->Map(c.constants.Get(), 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped), "Upload compositor opacity"); float values[] = { static_cast<float>(opacity), 0, 0, 0 }; std::memcpy(mapped.pData, values, sizeof(values)); c.context->Unmap(c.constants.Get(), 0);
    const double scale = std::min(static_cast<double>(c.width) / width, static_cast<double>(c.height) / height);
    const int fitted_width = std::clamp(static_cast<int>(std::nearbyint(width * scale)), 1, c.width); const int fitted_height = std::clamp(static_cast<int>(std::nearbyint(height * scale)), 1, c.height);
    D3D11_VIEWPORT viewport{}; viewport.Width = static_cast<float>(fitted_width); viewport.Height = static_cast<float>(fitted_height); viewport.TopLeftX = static_cast<float>((c.width - fitted_width) / 2); viewport.TopLeftY = static_cast<float>((c.height - fitted_height) / 2); viewport.MaxDepth = 1;
    c.context->RSSetViewports(1, &viewport); c.context->RSSetState(c.rasterizer.Get()); c.context->OMSetBlendState(c.blend.Get(), nullptr, 0xffffffff);
    ID3D11RenderTargetView* target = c.target.Get(); c.context->OMSetRenderTargets(1, &target, nullptr); c.context->IASetInputLayout(nullptr); c.context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST); c.context->VSSetShader(c.vertex.Get(), nullptr, 0); c.context->PSSetShader(c.pixel.Get(), nullptr, 0);
    ID3D11ShaderResourceView* source = c.source.Get(); c.context->PSSetShaderResources(0, 1, &source); ID3D11SamplerState* sampler = c.sampler.Get(); c.context->PSSetSamplers(0, 1, &sampler); ID3D11Buffer* constants = c.constants.Get(); c.context->PSSetConstantBuffers(0, 1, &constants); c.context->Draw(3, 0); return 0;
}); }
int or_compositor_readback(void* handle, uint8_t* bgra, int capacity) { return safe([&] {
    require(handle && bgra, "Invalid compositor readback arguments"); auto& c = *static_cast<Compositor*>(handle); std::lock_guard lock(c.mutex); require(int64_t(c.width) * c.height * 4 <= capacity, "Compositor readback buffer is too small");
    c.context->CopyResource(c.staging.Get(), c.output.Get()); D3D11_MAPPED_SUBRESOURCE mapped{}; check(c.context->Map(c.staging.Get(), 0, D3D11_MAP_READ, 0, &mapped), "Read compositor output");
    for (int y = 0; y < c.height; ++y) std::memcpy(bgra + static_cast<size_t>(y) * c.width * 4, static_cast<const uint8_t*>(mapped.pData) + static_cast<size_t>(y) * mapped.RowPitch, static_cast<size_t>(c.width) * 4);
    c.context->Unmap(c.staging.Get(), 0); return 0;
}); }
int or_compositor_is_hardware(void* handle) { return handle && static_cast<Compositor*>(handle)->hardware ? 1 : 0; }
void or_compositor_close(void* handle) { delete static_cast<Compositor*>(handle); }
}
