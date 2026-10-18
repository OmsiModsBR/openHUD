#include "D3D9Backend.h"

#include <d3d9.h>

#include <algorithm>
#include <cstdio>
#include <cstring>

namespace hud {

namespace {

constexpr DWORD kFvf = D3DFVF_XYZRHW | D3DFVF_DIFFUSE | D3DFVF_TEX1;
constexpr UINT kVertexMax = 65536;
constexpr UINT kIndexMax = 196608;
// Pre-transformed vertices address pixel corners half a pixel off in Direct3D 9.
constexpr float kHalfPixel = -0.5f;

struct GpuVertex {
    float x, y, z, rhw;
    DWORD color;
    float u, v;
};

struct StateValue {
    D3DRENDERSTATETYPE state;
    DWORD value;
};

// The render states the backend sets, with the value it needs.
const StateValue kRenderStates[] = {
    {D3DRS_ZENABLE, D3DZB_FALSE},
    {D3DRS_ZWRITEENABLE, FALSE},
    {D3DRS_ALPHATESTENABLE, FALSE},
    {D3DRS_CULLMODE, D3DCULL_NONE},
    {D3DRS_LIGHTING, FALSE},
    {D3DRS_FOGENABLE, FALSE},
    {D3DRS_STENCILENABLE, FALSE},
    {D3DRS_ALPHABLENDENABLE, TRUE},
    {D3DRS_SRCBLEND, D3DBLEND_SRCALPHA},
    {D3DRS_DESTBLEND, D3DBLEND_INVSRCALPHA},
    {D3DRS_BLENDOP, D3DBLENDOP_ADD},
    {D3DRS_SEPARATEALPHABLENDENABLE, FALSE},
    {D3DRS_SCISSORTESTENABLE, FALSE},
    {D3DRS_COLORWRITEENABLE, 0xF},
    {D3DRS_SHADEMODE, D3DSHADE_GOURAUD},
    {D3DRS_FILLMODE, D3DFILL_SOLID},
    {D3DRS_SPECULARENABLE, FALSE},
    {D3DRS_DITHERENABLE, FALSE},
    {D3DRS_CLIPPING, TRUE},
    {D3DRS_SRGBWRITEENABLE, FALSE},
    {D3DRS_WRAP0, 0},
};
constexpr int kRenderStateCount = static_cast<int>(sizeof(kRenderStates) / sizeof(kRenderStates[0]));

const D3DTEXTURESTAGESTATETYPE kStage0[] = {D3DTSS_COLOROP, D3DTSS_COLORARG1, D3DTSS_COLORARG2, D3DTSS_ALPHAOP,
                                            D3DTSS_ALPHAARG1, D3DTSS_ALPHAARG2, D3DTSS_TEXCOORDINDEX, D3DTSS_TEXTURETRANSFORMFLAGS};
constexpr int kStage0Count = static_cast<int>(sizeof(kStage0) / sizeof(kStage0[0]));

const D3DSAMPLERSTATETYPE kSampler0[] = {D3DSAMP_MINFILTER, D3DSAMP_MAGFILTER, D3DSAMP_MIPFILTER,
                                         D3DSAMP_ADDRESSU, D3DSAMP_ADDRESSV, D3DSAMP_SRGBTEXTURE};
constexpr int kSamplerCount = static_cast<int>(sizeof(kSampler0) / sizeof(kSampler0[0]));

template <typename T>
void SafeRelease(T*& p) {
    if (p != nullptr) {
        p->Release();
        p = nullptr;
    }
}

int NextPow2(int v) {
    int p = 1;
    while (p < v) p <<= 1;
    return p;
}

} // namespace

struct D3D9Backend::Saved {
    DWORD renderStates[kRenderStateCount] = {};
    DWORD stage0[kStage0Count] = {};
    DWORD stage1ColorOp = 0, stage1AlphaOp = 0;
    DWORD sampler0[kSamplerCount] = {};
    IDirect3DBaseTexture9* texture0 = nullptr;
    DWORD fvf = 0;
    IDirect3DVertexDeclaration9* declaration = nullptr;
    IDirect3DVertexShader9* vertexShader = nullptr;
    IDirect3DPixelShader9* pixelShader = nullptr;
    IDirect3DVertexBuffer9* stream0 = nullptr;
    UINT streamOffset = 0, streamStride = 0;
    IDirect3DIndexBuffer9* indices = nullptr;
    D3DVIEWPORT9 viewport = {};
    RECT scissor = {};   // SetViewport and SetRenderTarget overwrite it
    IDirect3DSurface9* renderTarget = nullptr;
    IDirect3DSurface9* backBuffer = nullptr;
    bool changedTarget = false;
};

D3D9Backend::D3D9Backend() = default;

D3D9Backend::~D3D9Backend() { Detach(); }

void D3D9Backend::Fail(const char* what, long hr) {
    char text[96];
    sprintf_s(text, "%s (0x%08lX)", what, static_cast<unsigned long>(hr));
    lastError_ = text;
}

void D3D9Backend::Attach(IDirect3DDevice9* device) {
    if (device_ == device) return;
    Detach();
    device_ = device;
}

void D3D9Backend::ReleaseDefaultPool() {
    SafeRelease(vertices_);
    SafeRelease(indices_);
    SafeRelease(background_);
}

void D3D9Backend::Detach() {
    if (saved_ != nullptr) EndFrame();
    ReleaseDefaultPool();
    for (Entry& e : textures_) SafeRelease(e.texture);
    textures_.clear();
    freeIds_.clear();
    device_ = nullptr;
}

bool D3D9Backend::EnsureBuffers() {
    if (vertices_ == nullptr) {
        const HRESULT hr = device_->CreateVertexBuffer(kVertexMax * sizeof(GpuVertex), D3DUSAGE_DYNAMIC | D3DUSAGE_WRITEONLY, kFvf, D3DPOOL_DEFAULT, &vertices_, nullptr);
        if (FAILED(hr)) { Fail("CreateVertexBuffer", hr); return false; }
    }
    if (indices_ == nullptr) {
        const HRESULT hr = device_->CreateIndexBuffer(kIndexMax * sizeof(uint16_t), D3DUSAGE_DYNAMIC | D3DUSAGE_WRITEONLY, D3DFMT_INDEX16, D3DPOOL_DEFAULT, &indices_, nullptr);
        if (FAILED(hr)) { Fail("CreateIndexBuffer", hr); return false; }
    }
    return true;
}

bool D3D9Backend::BeginFrame(int& width, int& height) {
    if (device_ == nullptr || saved_ != nullptr) return false;
    if (FAILED(device_->TestCooperativeLevel())) { ++skipped_; return false; }
    IDirect3DSurface9* backBuffer = nullptr;
    if (FAILED(device_->GetBackBuffer(0, 0, D3DBACKBUFFER_TYPE_MONO, &backBuffer)) || backBuffer == nullptr) { ++skipped_; return false; }
    D3DSURFACE_DESC desc = {};
    backBuffer->GetDesc(&desc);
    if (desc.Width == 0 || desc.Height == 0 || !EnsureBuffers()) {
        backBuffer->Release();
        ++skipped_;
        return false;
    }
    auto* s = new Saved();
    s->backBuffer = backBuffer;
    for (int i = 0; i < kRenderStateCount; ++i) device_->GetRenderState(kRenderStates[i].state, &s->renderStates[i]);
    for (int i = 0; i < kStage0Count; ++i) device_->GetTextureStageState(0, kStage0[i], &s->stage0[i]);
    device_->GetTextureStageState(1, D3DTSS_COLOROP, &s->stage1ColorOp);
    device_->GetTextureStageState(1, D3DTSS_ALPHAOP, &s->stage1AlphaOp);
    for (int i = 0; i < kSamplerCount; ++i) device_->GetSamplerState(0, kSampler0[i], &s->sampler0[i]);
    device_->GetTexture(0, &s->texture0);
    device_->GetFVF(&s->fvf);
    device_->GetVertexDeclaration(&s->declaration);
    device_->GetVertexShader(&s->vertexShader);
    device_->GetPixelShader(&s->pixelShader);
    device_->GetStreamSource(0, &s->stream0, &s->streamOffset, &s->streamStride);
    device_->GetIndices(&s->indices);
    device_->GetViewport(&s->viewport);
    device_->GetScissorRect(&s->scissor);
    device_->GetRenderTarget(0, &s->renderTarget);
    saved_ = s;

    if (s->renderTarget != backBuffer) {
        device_->SetRenderTarget(0, backBuffer);
        s->changedTarget = true;
    }
    D3DVIEWPORT9 viewport = {0, 0, desc.Width, desc.Height, 0.0f, 1.0f};
    device_->SetViewport(&viewport);
    for (int i = 0; i < kRenderStateCount; ++i) device_->SetRenderState(kRenderStates[i].state, kRenderStates[i].value);
    device_->SetVertexShader(nullptr);
    device_->SetPixelShader(nullptr);
    device_->SetFVF(kFvf);
    device_->SetStreamSource(0, vertices_, 0, sizeof(GpuVertex));
    device_->SetIndices(indices_);
    device_->SetTextureStageState(0, D3DTSS_TEXCOORDINDEX, 0);
    device_->SetTextureStageState(0, D3DTSS_TEXTURETRANSFORMFLAGS, D3DTTFF_DISABLE);
    device_->SetTextureStageState(0, D3DTSS_COLORARG1, D3DTA_TEXTURE);
    device_->SetTextureStageState(0, D3DTSS_COLORARG2, D3DTA_DIFFUSE);
    device_->SetTextureStageState(0, D3DTSS_ALPHAARG1, D3DTA_TEXTURE);
    device_->SetTextureStageState(0, D3DTSS_ALPHAARG2, D3DTA_DIFFUSE);
    device_->SetTextureStageState(1, D3DTSS_COLOROP, D3DTOP_DISABLE);
    device_->SetTextureStageState(1, D3DTSS_ALPHAOP, D3DTOP_DISABLE);
    device_->SetSamplerState(0, D3DSAMP_MINFILTER, D3DTEXF_LINEAR);
    device_->SetSamplerState(0, D3DSAMP_MAGFILTER, D3DTEXF_LINEAR);
    device_->SetSamplerState(0, D3DSAMP_MIPFILTER, D3DTEXF_NONE);
    device_->SetSamplerState(0, D3DSAMP_ADDRESSU, D3DTADDRESS_CLAMP);
    device_->SetSamplerState(0, D3DSAMP_ADDRESSV, D3DTADDRESS_CLAMP);
    device_->SetSamplerState(0, D3DSAMP_SRGBTEXTURE, FALSE);
    stateKnown_ = false;
    SetTextureMode(false);
    // BeginScene fails when the game is inside a scene already; drawing is allowed then.
    sceneBegun_ = SUCCEEDED(device_->BeginScene());
    viewW_ = width = static_cast<int>(desc.Width);
    viewH_ = height = static_cast<int>(desc.Height);
    viewWidth_.store(viewW_);
    viewHeight_.store(viewH_);
    return true;
}

void D3D9Backend::EndFrame() {
    Saved* s = saved_;
    if (s == nullptr) return;
    saved_ = nullptr;
    if (sceneBegun_) device_->EndScene();
    sceneBegun_ = false;
    for (int i = 0; i < kRenderStateCount; ++i) device_->SetRenderState(kRenderStates[i].state, s->renderStates[i]);
    for (int i = 0; i < kStage0Count; ++i) device_->SetTextureStageState(0, kStage0[i], s->stage0[i]);
    device_->SetTextureStageState(1, D3DTSS_COLOROP, s->stage1ColorOp);
    device_->SetTextureStageState(1, D3DTSS_ALPHAOP, s->stage1AlphaOp);
    for (int i = 0; i < kSamplerCount; ++i) device_->SetSamplerState(0, kSampler0[i], s->sampler0[i]);
    device_->SetTexture(0, s->texture0);
    if (s->declaration != nullptr) device_->SetVertexDeclaration(s->declaration);
    else device_->SetFVF(s->fvf);
    device_->SetVertexShader(s->vertexShader);
    device_->SetPixelShader(s->pixelShader);
    device_->SetStreamSource(0, s->stream0, s->streamOffset, s->streamStride);
    device_->SetIndices(s->indices);
    if (s->changedTarget) device_->SetRenderTarget(0, s->renderTarget);   // before the viewport: SetRenderTarget resets it
    device_->SetViewport(&s->viewport);
    device_->SetScissorRect(&s->scissor);
    SafeRelease(s->texture0);
    SafeRelease(s->declaration);
    SafeRelease(s->vertexShader);
    SafeRelease(s->pixelShader);
    SafeRelease(s->stream0);
    SafeRelease(s->indices);
    SafeRelease(s->renderTarget);
    SafeRelease(s->backBuffer);
    delete s;
}

void D3D9Backend::SetTextureMode(bool textured) {
    if (stateKnown_ && textured == textured_) return;
    device_->SetTextureStageState(0, D3DTSS_COLOROP, textured ? D3DTOP_MODULATE : D3DTOP_SELECTARG2);
    device_->SetTextureStageState(0, D3DTSS_ALPHAOP, textured ? D3DTOP_MODULATE : D3DTOP_SELECTARG2);
    textured_ = textured;
    stateKnown_ = true;
}

bool D3D9Backend::CreateTexture(int w, int h, const uint8_t* rgba, TextureRef& out) {
    if (device_ == nullptr || w < 1 || h < 1 || w > 8192 || h > 8192) return false;
    const int tw = NextPow2(w), th = NextPow2(h);
    IDirect3DTexture9* texture = nullptr;
    const HRESULT hr = device_->CreateTexture(static_cast<UINT>(tw), static_cast<UINT>(th), 1, 0, D3DFMT_A8R8G8B8, D3DPOOL_MANAGED, &texture, nullptr);
    if (FAILED(hr)) { Fail("CreateTexture", hr); return false; }
    D3DLOCKED_RECT lr;
    if (FAILED(texture->LockRect(0, &lr, nullptr, 0))) { texture->Release(); return false; }
    for (int y = 0; y < th; ++y) {
        uint8_t* row = static_cast<uint8_t*>(lr.pBits) + static_cast<size_t>(y) * static_cast<size_t>(lr.Pitch);
        const int sy = std::min(y, h - 1);   // padding repeats the last row and column so bilinear edges stay solid
        for (int x = 0; x < tw; ++x) {
            const int sx = std::min(x, w - 1);
            uint8_t px[4] = {0, 0, 0, 0};
            if (rgba != nullptr) std::memcpy(px, rgba + (static_cast<size_t>(sy) * static_cast<size_t>(w) + static_cast<size_t>(sx)) * 4, 4);
            row[x * 4 + 0] = px[2];
            row[x * 4 + 1] = px[1];
            row[x * 4 + 2] = px[0];
            row[x * 4 + 3] = px[3];
        }
    }
    texture->UnlockRect(0);
    int id;
    Entry entry;
    entry.texture = texture;
    entry.width = w;
    entry.height = h;
    if (!freeIds_.empty()) {
        id = freeIds_.back();
        freeIds_.pop_back();
        textures_[static_cast<size_t>(id - 1)] = entry;
    } else {
        textures_.push_back(entry);
        id = static_cast<int>(textures_.size());
    }
    out.id = id;
    out.u1 = static_cast<float>(w) / static_cast<float>(tw);
    out.v1 = static_cast<float>(h) / static_cast<float>(th);
    return true;
}

void D3D9Backend::UpdateTexture(int id, int x, int y, int w, int h, const uint8_t* rgba, int pitchBytes) {
    if (id < 1 || id > static_cast<int>(textures_.size())) return;
    IDirect3DTexture9* texture = textures_[static_cast<size_t>(id - 1)].texture;
    if (texture == nullptr || w < 1 || h < 1) return;
    D3DSURFACE_DESC desc;
    texture->GetLevelDesc(0, &desc);
    if (x < 0 || y < 0 || x + w > static_cast<int>(desc.Width) || y + h > static_cast<int>(desc.Height)) return;
    const RECT rect = {x, y, x + w, y + h};
    D3DLOCKED_RECT lr;
    if (FAILED(texture->LockRect(0, &lr, &rect, 0))) return;
    for (int row = 0; row < h; ++row) {
        uint8_t* dst = static_cast<uint8_t*>(lr.pBits) + static_cast<size_t>(row) * static_cast<size_t>(lr.Pitch);
        const uint8_t* src = rgba + static_cast<size_t>(row) * static_cast<size_t>(pitchBytes);
        for (int col = 0; col < w; ++col) {
            dst[col * 4 + 0] = src[col * 4 + 2];
            dst[col * 4 + 1] = src[col * 4 + 1];
            dst[col * 4 + 2] = src[col * 4 + 0];
            dst[col * 4 + 3] = src[col * 4 + 3];
        }
    }
    texture->UnlockRect(0);
}

void D3D9Backend::DestroyTexture(int id) {
    if (id < 1 || id > static_cast<int>(textures_.size())) return;
    Entry& e = textures_[static_cast<size_t>(id - 1)];
    if (e.texture == nullptr) return;
    SafeRelease(e.texture);
    freeIds_.push_back(id);
}

bool D3D9Backend::DrawRaw(const Vtx* vertices, int vertexCount, const uint16_t* indices, int indexCount, IDirect3DTexture9* texture, bool opaque) {
    if (saved_ == nullptr || vertexCount < 3 || indexCount < 3) return false;
    if (static_cast<UINT>(vertexCount) > kVertexMax || static_cast<UINT>(indexCount) > kIndexMax) return false;
    void* data = nullptr;
    HRESULT hr = vertices_->Lock(0, static_cast<UINT>(vertexCount) * sizeof(GpuVertex), &data, D3DLOCK_DISCARD);
    if (FAILED(hr)) { Fail("vertex buffer lock", hr); return false; }
    GpuVertex* out = static_cast<GpuVertex*>(data);
    for (int i = 0; i < vertexCount; ++i) {
        out[i] = {vertices[i].x + kHalfPixel, vertices[i].y + kHalfPixel, 0.0f, 1.0f, vertices[i].color, vertices[i].u, vertices[i].v};
    }
    vertices_->Unlock();
    hr = indices_->Lock(0, static_cast<UINT>(indexCount) * sizeof(uint16_t), &data, D3DLOCK_DISCARD);
    if (FAILED(hr)) { Fail("index buffer lock", hr); return false; }
    std::memcpy(data, indices, static_cast<size_t>(indexCount) * sizeof(uint16_t));
    indices_->Unlock();
    SetTextureMode(texture != nullptr);
    if (opaque) {   // the captured frame is opaque whatever its alpha channel holds
        device_->SetTextureStageState(0, D3DTSS_ALPHAOP, D3DTOP_SELECTARG2);
        stateKnown_ = false;
    }
    device_->SetTexture(0, texture);
    hr = device_->DrawIndexedPrimitive(D3DPT_TRIANGLELIST, 0, 0, static_cast<UINT>(vertexCount), 0, static_cast<UINT>(indexCount / 3));
    if (FAILED(hr)) { Fail("DrawIndexedPrimitive", hr); return false; }
    return true;
}

bool D3D9Backend::Draw(const Vtx* vertices, int vertexCount, const uint16_t* indices, int indexCount, int texture) {
    IDirect3DTexture9* tex = nullptr;
    if (texture > 0) {
        if (texture > static_cast<int>(textures_.size())) return false;
        tex = textures_[static_cast<size_t>(texture - 1)].texture;
        if (tex == nullptr) return false;
    }
    return DrawRaw(vertices, vertexCount, indices, indexCount, tex, false);
}

bool D3D9Backend::CaptureBackground() {
    if (device_ == nullptr) return false;
    IDirect3DSurface9* backBuffer = nullptr;
    if (FAILED(device_->GetBackBuffer(0, 0, D3DBACKBUFFER_TYPE_MONO, &backBuffer)) || backBuffer == nullptr) return false;
    D3DSURFACE_DESC desc = {};
    backBuffer->GetDesc(&desc);
    SafeRelease(background_);
    IDirect3DTexture9* texture = nullptr;
    HRESULT hr = device_->CreateTexture(desc.Width, desc.Height, 1, D3DUSAGE_RENDERTARGET, desc.Format, D3DPOOL_DEFAULT, &texture, nullptr);
    bool ok = false;
    if (FAILED(hr)) {
        Fail("CreateTexture (background)", hr);
    } else {
        IDirect3DSurface9* target = nullptr;
        if (SUCCEEDED(texture->GetSurfaceLevel(0, &target))) {
            hr = device_->StretchRect(backBuffer, nullptr, target, nullptr, D3DTEXF_NONE);
            if (FAILED(hr)) Fail("StretchRect (background)", hr);
            else ok = true;
            target->Release();
        }
    }
    backBuffer->Release();
    if (ok) {
        background_ = texture;
    } else {
        SafeRelease(texture);
    }
    return ok;
}

void D3D9Backend::ReleaseBackground() { SafeRelease(background_); }

void D3D9Backend::DrawBackground() {
    if (background_ == nullptr || saved_ == nullptr) return;
    const float w = static_cast<float>(viewW_), h = static_cast<float>(viewH_);
    const uint32_t white = 0xFFFFFFFFu;
    const Vtx quad[4] = {{0, 0, 0, 0, white}, {w, 0, 1, 0, white}, {w, h, 1, 1, white}, {0, h, 0, 1, white}};
    static const uint16_t order[6] = {0, 1, 2, 0, 2, 3};
    DrawRaw(quad, 4, order, 6, background_, true);
}

} // namespace hud
