#include "Kit.h"

#include "D3D9Backend.h"
#include "DrawLayer.h"
#include "FakeBridge.h"

#include <d3d9.h>

using namespace hud;
using namespace kit;

namespace {

// A hidden window with a Direct3D 9 device shaped like the game's: windowed, discard swap,
// no flags, D24S8 automatic depth-stencil, immediate presentation, multithreaded.
struct Env {
    HWND window = nullptr;
    IDirect3D9* d3d = nullptr;
    IDirect3DDevice9* device = nullptr;
    D3DPRESENT_PARAMETERS pp = {};
    std::string why;
    std::string kind;
    static int counter;

    ~Env() {
        if (device != nullptr) device->Release();
        if (d3d != nullptr) d3d->Release();
        if (window != nullptr) DestroyWindow(window);
    }

    bool Create(int w, int h) {
        static bool registered = false;
        const wchar_t* name = L"OpenHudDrawTestWindow";
        if (!registered) {
            WNDCLASSW wc = {};
            wc.lpfnWndProc = DefWindowProcW;
            wc.hInstance = GetModuleHandleW(nullptr);
            wc.lpszClassName = name;
            RegisterClassW(&wc);
            registered = true;
        }
        window = CreateWindowExW(0, name, L"openHUD draw test", WS_OVERLAPPEDWINDOW, 0, 0, w + 20, h + 40, nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
        if (window == nullptr) { why = "cannot create the hidden test window"; return false; }
        d3d = Direct3DCreate9(D3D_SDK_VERSION);
        if (d3d == nullptr) { why = "Direct3D 9 is not available (Direct3DCreate9 failed)"; return false; }
        pp = {};
        pp.BackBufferWidth = static_cast<UINT>(w);
        pp.BackBufferHeight = static_cast<UINT>(h);
        pp.BackBufferFormat = D3DFMT_UNKNOWN;
        pp.SwapEffect = D3DSWAPEFFECT_DISCARD;
        pp.hDeviceWindow = window;
        pp.Windowed = TRUE;
        pp.EnableAutoDepthStencil = TRUE;
        pp.AutoDepthStencilFormat = D3DFMT_D24S8;
        pp.Flags = 0;
        pp.PresentationInterval = D3DPRESENT_INTERVAL_IMMEDIATE;
        struct Attempt { D3DDEVTYPE type; DWORD behavior; const char* label; };
        const Attempt attempts[] = {
            {D3DDEVTYPE_HAL, D3DCREATE_HARDWARE_VERTEXPROCESSING | D3DCREATE_MULTITHREADED, "HAL (hardware vertex processing)"},
            {D3DDEVTYPE_HAL, D3DCREATE_SOFTWARE_VERTEXPROCESSING | D3DCREATE_MULTITHREADED, "HAL (software vertex processing)"},
            {D3DDEVTYPE_REF, D3DCREATE_SOFTWARE_VERTEXPROCESSING | D3DCREATE_MULTITHREADED, "REF"},
        };
        std::string errors;
        for (const Attempt& a : attempts) {
            const HRESULT hr = d3d->CreateDevice(D3DADAPTER_DEFAULT, a.type, window, a.behavior, &pp, &device);
            if (SUCCEEDED(hr)) { kind = a.label; return true; }
            char text[96];
            sprintf_s(text, "%s: 0x%08lX; ", a.label, static_cast<unsigned long>(hr));
            errors += text;
            device = nullptr;
            pp.BackBufferWidth = static_cast<UINT>(w);
            pp.BackBufferHeight = static_cast<UINT>(h);
        }
        why = "no Direct3D 9 device could be created on a hidden window (" + errors + ")";
        return false;
    }
};

void RequireDevice(Env& env, int w, int h) {
    if (!env.Create(w, h)) SKIP(env.why);
}

bool ReadBack(IDirect3DDevice9* device, int& w, int& h, std::vector<uint8_t>& rgba, std::string& why) {
    IDirect3DSurface9* back = nullptr;
    if (FAILED(device->GetBackBuffer(0, 0, D3DBACKBUFFER_TYPE_MONO, &back))) { why = "GetBackBuffer failed"; return false; }
    D3DSURFACE_DESC desc;
    back->GetDesc(&desc);
    if (desc.Format != D3DFMT_X8R8G8B8 && desc.Format != D3DFMT_A8R8G8B8) {
        back->Release();
        why = "the back buffer format is not 32-bit";
        return false;
    }
    IDirect3DSurface9* system = nullptr;
    HRESULT hr = device->CreateOffscreenPlainSurface(desc.Width, desc.Height, desc.Format, D3DPOOL_SYSTEMMEM, &system, nullptr);
    if (SUCCEEDED(hr)) hr = device->GetRenderTargetData(back, system);
    back->Release();
    if (FAILED(hr)) {
        if (system != nullptr) system->Release();
        why = "GetRenderTargetData failed";
        return false;
    }
    D3DLOCKED_RECT lr;
    if (FAILED(system->LockRect(&lr, nullptr, D3DLOCK_READONLY))) { system->Release(); why = "LockRect failed"; return false; }
    w = static_cast<int>(desc.Width);
    h = static_cast<int>(desc.Height);
    rgba.assign(static_cast<size_t>(w) * static_cast<size_t>(h) * 4, 0);
    for (int y = 0; y < h; ++y) {
        const uint8_t* row = static_cast<const uint8_t*>(lr.pBits) + static_cast<size_t>(y) * static_cast<size_t>(lr.Pitch);
        for (int x = 0; x < w; ++x) {
            uint8_t* o = &rgba[(static_cast<size_t>(y) * static_cast<size_t>(w) + static_cast<size_t>(x)) * 4];
            o[0] = row[x * 4 + 2];
            o[1] = row[x * 4 + 1];
            o[2] = row[x * 4];
            o[3] = 255;
        }
    }
    system->UnlockRect();
    system->Release();
    return true;
}

Rgba At(const std::vector<uint8_t>& px, int w, int x, int y) {
    const uint8_t* p = &px[(static_cast<size_t>(y) * static_cast<size_t>(w) + static_cast<size_t>(x)) * 4];
    return {p[0], p[1], p[2], p[3]};
}

void Clear(IDirect3DDevice9* device, uint32_t argb) {
    device->Clear(0, nullptr, D3DCLEAR_TARGET | D3DCLEAR_ZBUFFER | D3DCLEAR_STENCIL, argb, 1.0f, 0);
}

struct Difference {
    int maxDiff = 0;
    int over = 0;       // pixels with a channel off by more than 8
    int firstX = -1, firstY = -1;
    double fraction = 0;
};

Difference Compare(const std::vector<uint8_t>& a, const std::vector<uint8_t>& b, int w, int h) {
    Difference d;
    for (int y = 0; y < h; ++y) {
        for (int x = 0; x < w; ++x) {
            const Rgba p = At(a, w, x, y), q = At(b, w, x, y);
            const int m = std::max({std::abs(p.r - q.r), std::abs(p.g - q.g), std::abs(p.b - q.b)});
            d.maxDiff = std::max(d.maxDiff, m);
            if (m > 8) {
                if (d.over == 0) { d.firstX = x; d.firstY = y; }
                ++d.over;
            }
        }
    }
    d.fraction = static_cast<double>(d.over) / static_cast<double>(w * h);
    return d;
}

std::string Vector(const char* name) {
    bool ok = false;
    const std::string text = t::ReadFile(t::GetConfig().vectorsDirectory + "/" + name, ok);
    if (!ok) SKIP("the golden vectors directory was not given (--vectors)");
    return text;
}

const std::vector<uint8_t> kPlayerImage = {255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 255};

// Everything the renderer can do on a 480x270 canvas.
std::string TortureScene() {
    return "{\"v\":1,\"w\":480,\"h\":270,\"background\":false,\"cmds\":["
           "{\"t\":\"rect\",\"x\":10.5,\"y\":10.25,\"w\":120,\"h\":60,\"fill\":\"#2a3b4cc0\",\"stroke\":\"#ffcc00\",\"sw\":3,\"r\":14},"
           "{\"t\":\"circle\",\"cx\":200,\"cy\":50,\"r\":30,\"fill\":\"#aa3322\",\"stroke\":\"#ffffff80\",\"sw\":5},"
           "{\"t\":\"arc\",\"cx\":300,\"cy\":50,\"r\":30,\"a0\":-120,\"a1\":100,\"color\":\"#33dd88\",\"sw\":8,\"cap\":\"round\"},"
           "{\"t\":\"arc\",\"cx\":380,\"cy\":50,\"r\":30,\"a0\":20,\"a1\":300,\"color\":\"#88aaff\",\"sw\":6,\"cap\":\"butt\"},"
           "{\"t\":\"line\",\"x1\":10,\"y1\":100,\"x2\":150,\"y2\":130,\"color\":\"#ffffff\",\"sw\":1},"
           "{\"t\":\"line\",\"x1\":10,\"y1\":110,\"x2\":150,\"y2\":150,\"color\":\"#ff88ff\",\"sw\":7,\"cap\":\"round\"},"
           "{\"t\":\"poly\",\"pts\":[170,100,250,100,250,130,200,130,200,170,170,170],\"fill\":\"#44aa44cc\",\"stroke\":\"#003300\",\"sw\":2},"
           "{\"t\":\"clip\",\"kind\":\"circle\",\"cx\":330,\"cy\":140,\"r\":45},"
           "{\"t\":\"clip\",\"kind\":\"rect\",\"x\":300,\"y\":100,\"w\":100,\"h\":60},"
           "{\"t\":\"push\",\"tx\":330,\"ty\":140,\"rot\":30},"
           "{\"t\":\"rect\",\"x\":-60,\"y\":-60,\"w\":120,\"h\":120,\"fill\":\"#ffffff60\",\"stroke\":\"#ff0000\",\"sw\":4},"
           "{\"t\":\"pop\"},{\"t\":\"unclip\"},{\"t\":\"unclip\"},"
           "{\"t\":\"image\",\"id\":\"player\",\"x\":420,\"y\":100,\"w\":40,\"h\":30,\"op\":0.8,\"tint\":\"#ffeecc\",\"rot\":25},"
           "{\"t\":\"image\",\"id\":\"player\",\"x\":420,\"y\":150,\"w\":30,\"h\":30},"
           "{\"t\":\"text\",\"x\":10,\"y\":190,\"s\":\"Hello, openHUD 123\",\"size\":22,\"font\":\"bold\",\"color\":\"#ffffff\"},"
           "{\"t\":\"text\",\"x\":10,\"y\":220,\"s\":\"Small caption text\",\"size\":12,\"color\":\"#ffee88\",\"outline\":\"#000000c0\"},"
           "{\"t\":\"text\",\"x\":470,\"y\":190,\"s\":\"Right aligned\",\"size\":18,\"color\":\"#99ccff\",\"align\":\"r\"},"
           "{\"t\":\"text\",\"x\":240,\"y\":245,\"s\":\"A long line that needs to be cut with an ellipsis\",\"size\":16,\"color\":\"#ffffff\",\"align\":\"c\",\"base\":\"mid\",\"maxw\":220}"
           "]}";
}

} // namespace

TEST(d3d9_vs_software, "d3d9.matches-the-software-backend") {
    Env env;
    RequireDevice(env, 480, 270);
    const struct { const char* name; std::string json; bool background; } scenes[] = {
        {"minimal", Vector("scene-minimal.json"), false},
        {"radar", Vector("scene-radar.json"), false},
        {"pause", Vector("scene-pause.json"), true},
        {"torture", TortureScene(), false},
    };
    for (const auto& scene : scenes) {
        FontRegistry fonts;
        GdiGlyphSource glyphs(fonts);
        RegisterTestFonts(fonts);
        // Software reference.
        SoftBackend soft(480, 270);
        Renderer softRenderer(glyphs);
        softRenderer.PutImage("player", 2, 2, std::vector<uint8_t>(kPlayerImage));
        // Direct3D.
        D3D9Backend backend;
        backend.Attach(env.device);
        Renderer d3dRenderer(glyphs);
        d3dRenderer.PutImage("player", 2, 2, std::vector<uint8_t>(kPlayerImage));
        SceneError error;
        Scene parsed;
        CHECK(ParseScene(scene.json, parsed, error));
        const auto shared = std::make_shared<const Scene>(parsed);
        softRenderer.SetScene(shared);
        d3dRenderer.SetScene(shared);

        if (scene.background) {
            soft.Clear(0xFFC80000u);
            soft.CaptureBackground();
            soft.Clear(0xFF00FF00u);
            Clear(env.device, 0xFFC80000u);
            CHECK(backend.CaptureBackground());
            Clear(env.device, 0xFF00FF00u);
        } else {
            soft.Clear(0xFF808080u);
            Clear(env.device, 0xFF808080u);
        }
        CHECK(softRenderer.Render(soft) == RenderResult::Drawn);
        const RenderResult result = d3dRenderer.Render(backend);
        CHECK_MSG(result == RenderResult::Drawn, std::string(scene.name) + ": " + backend.LastError());
        int w = 0, h = 0;
        std::vector<uint8_t> gpu;
        std::string why;
        if (!ReadBack(env.device, w, h, gpu, why)) SKIP(why);
        CHECK(w == 480 && h == 270);
        const Difference d = Compare(gpu, soft.Pixels(), w, h);
        std::printf("  d3d9 %s on %s: %d of %d pixels differ by more than 8 (max channel difference %d)\n", scene.name, env.kind.c_str(), d.over, w * h, d.maxDiff);
        CHECK_MSG(d.fraction < 0.002, std::string(scene.name) + " on " + env.kind + ": " + std::to_string(d.over) + " pixels differ (max " + std::to_string(d.maxDiff) +
                                         ", first at " + std::to_string(d.firstX) + "," + std::to_string(d.firstY) + ")");
        // And the Direct3D output really contains the scene.
        const Box drawn = Changed(soft, 0, 0, 480, 270, scene.background ? Rgba{0, 255, 0, 255} : Rgba{128, 128, 128, 255}, 30);
        CHECK(drawn.count > 300);
        backend.Detach();
    }
}

TEST(d3d9_state, "d3d9.restores-only-what-it-changes") {
    Env env;
    RequireDevice(env, 320, 240);
    IDirect3DDevice9* dev = env.device;
    // Put the device in a state unlike the one the backend needs.
    dev->SetRenderState(D3DRS_ALPHABLENDENABLE, FALSE);
    dev->SetRenderState(D3DRS_SRCBLEND, D3DBLEND_ONE);
    dev->SetRenderState(D3DRS_DESTBLEND, D3DBLEND_ZERO);
    dev->SetRenderState(D3DRS_CULLMODE, D3DCULL_CW);
    dev->SetRenderState(D3DRS_ZENABLE, D3DZB_TRUE);
    dev->SetRenderState(D3DRS_ZWRITEENABLE, TRUE);
    dev->SetRenderState(D3DRS_LIGHTING, TRUE);
    dev->SetRenderState(D3DRS_FOGENABLE, TRUE);
    dev->SetRenderState(D3DRS_STENCILENABLE, TRUE);
    dev->SetRenderState(D3DRS_SCISSORTESTENABLE, TRUE);
    dev->SetRenderState(D3DRS_COLORWRITEENABLE, 0x7);
    dev->SetRenderState(D3DRS_FILLMODE, D3DFILL_WIREFRAME);
    dev->SetRenderState(D3DRS_ALPHATESTENABLE, TRUE);
    dev->SetRenderState(D3DRS_WRAP0, D3DWRAP_U);
    dev->SetTextureStageState(0, D3DTSS_COLOROP, D3DTOP_ADD);
    dev->SetTextureStageState(0, D3DTSS_ALPHAOP, D3DTOP_DISABLE);
    dev->SetTextureStageState(0, D3DTSS_COLORARG1, D3DTA_CURRENT);
    dev->SetTextureStageState(0, D3DTSS_TEXCOORDINDEX, 1);
    dev->SetTextureStageState(1, D3DTSS_COLOROP, D3DTOP_MODULATE);
    dev->SetSamplerState(0, D3DSAMP_MINFILTER, D3DTEXF_POINT);
    dev->SetSamplerState(0, D3DSAMP_ADDRESSU, D3DTADDRESS_WRAP);
    dev->SetFVF(D3DFVF_XYZ | D3DFVF_NORMAL);
    IDirect3DTexture9* tex = nullptr;
    CHECK(SUCCEEDED(dev->CreateTexture(8, 8, 1, 0, D3DFMT_A8R8G8B8, D3DPOOL_MANAGED, &tex, nullptr)));
    dev->SetTexture(0, tex);
    IDirect3DVertexBuffer9* vb = nullptr;
    CHECK(SUCCEEDED(dev->CreateVertexBuffer(1024, 0, D3DFVF_XYZ | D3DFVF_NORMAL, D3DPOOL_MANAGED, &vb, nullptr)));
    dev->SetStreamSource(0, vb, 16, 24);
    // A second render target that is not the back buffer.
    IDirect3DTexture9* targetTexture = nullptr;
    IDirect3DSurface9* target = nullptr;
    if (SUCCEEDED(dev->CreateTexture(320, 240, 1, D3DUSAGE_RENDERTARGET, D3DFMT_X8R8G8B8, D3DPOOL_DEFAULT, &targetTexture, nullptr)) && SUCCEEDED(targetTexture->GetSurfaceLevel(0, &target)))
        dev->SetRenderTarget(0, target);
    D3DVIEWPORT9 vp = {10, 20, 100, 80, 0.25f, 0.75f};
    dev->SetViewport(&vp);
    RECT scissor = {5, 5, 50, 50};
    dev->SetScissorRect(&scissor);

    FontRegistry fonts;
    GdiGlyphSource glyphs(fonts);
    RegisterTestFonts(fonts);
    Renderer renderer(glyphs);
    D3D9Backend backend;
    backend.Attach(dev);
    renderer.SetScene(std::make_shared<const Scene>([] {
        Scene s;
        SceneError e;
        ParseScene("{\"v\":1,\"w\":320,\"h\":240,\"cmds\":[{\"t\":\"rect\",\"x\":10,\"y\":10,\"w\":100,\"h\":50,\"fill\":\"#ffffff\"},{\"t\":\"text\",\"x\":10,\"y\":80,\"s\":\"state\",\"size\":20}]}", s, e);
        return s;
    }()));
    CHECK(renderer.Render(backend) == RenderResult::Drawn);

    DWORD v = 0;
    const auto rs = [&](D3DRENDERSTATETYPE s) { dev->GetRenderState(s, &v); return v; };
    CHECK(rs(D3DRS_ALPHABLENDENABLE) == FALSE && rs(D3DRS_SRCBLEND) == D3DBLEND_ONE && rs(D3DRS_DESTBLEND) == D3DBLEND_ZERO);
    CHECK(rs(D3DRS_CULLMODE) == D3DCULL_CW && rs(D3DRS_ZENABLE) == D3DZB_TRUE && rs(D3DRS_ZWRITEENABLE) == TRUE);
    CHECK(rs(D3DRS_LIGHTING) == TRUE && rs(D3DRS_FOGENABLE) == TRUE && rs(D3DRS_STENCILENABLE) == TRUE && rs(D3DRS_SCISSORTESTENABLE) == TRUE);
    CHECK(rs(D3DRS_COLORWRITEENABLE) == 0x7 && rs(D3DRS_FILLMODE) == D3DFILL_WIREFRAME && rs(D3DRS_ALPHATESTENABLE) == TRUE && rs(D3DRS_WRAP0) == D3DWRAP_U);
    const auto ts = [&](DWORD stage, D3DTEXTURESTAGESTATETYPE s) { dev->GetTextureStageState(stage, s, &v); return v; };
    CHECK(ts(0, D3DTSS_COLOROP) == D3DTOP_ADD && ts(0, D3DTSS_ALPHAOP) == D3DTOP_DISABLE && ts(0, D3DTSS_COLORARG1) == D3DTA_CURRENT && ts(0, D3DTSS_TEXCOORDINDEX) == 1);
    CHECK(ts(1, D3DTSS_COLOROP) == D3DTOP_MODULATE);
    dev->GetSamplerState(0, D3DSAMP_MINFILTER, &v);
    CHECK(v == D3DTEXF_POINT);
    dev->GetSamplerState(0, D3DSAMP_ADDRESSU, &v);
    CHECK(v == D3DTADDRESS_WRAP);
    DWORD fvf = 0;
    dev->GetFVF(&fvf);
    CHECK(fvf == (D3DFVF_XYZ | D3DFVF_NORMAL));
    IDirect3DBaseTexture9* bound = nullptr;
    dev->GetTexture(0, &bound);
    CHECK(bound == tex);
    if (bound) bound->Release();
    IDirect3DVertexBuffer9* stream = nullptr;
    UINT offset = 0, stride = 0;
    dev->GetStreamSource(0, &stream, &offset, &stride);
    CHECK(stream == vb && offset == 16 && stride == 24);
    if (stream) stream->Release();
    IDirect3DIndexBuffer9* indices = nullptr;
    dev->GetIndices(&indices);
    CHECK(indices == nullptr);
    D3DVIEWPORT9 now = {};
    dev->GetViewport(&now);
    CHECK(now.X == 10 && now.Y == 20 && now.Width == 100 && now.Height == 80 && now.MinZ == 0.25f && now.MaxZ == 0.75f);
    RECT scissorNow = {};
    dev->GetScissorRect(&scissorNow);
    CHECK(scissorNow.left == 5 && scissorNow.right == 50);
    if (target != nullptr) {
        IDirect3DSurface9* rt = nullptr;
        dev->GetRenderTarget(0, &rt);
        CHECK(rt == target);
        if (rt) rt->Release();
    }
    // The vertex shader and pixel shader slots stay empty as they were.
    IDirect3DVertexShader9* vs = nullptr;
    IDirect3DPixelShader9* ps = nullptr;
    dev->GetVertexShader(&vs);
    dev->GetPixelShader(&ps);
    CHECK(vs == nullptr && ps == nullptr);

    backend.Detach();
    dev->SetTexture(0, nullptr);
    dev->SetStreamSource(0, nullptr, 0, 0);
    if (target != nullptr) {
        IDirect3DSurface9* back = nullptr;
        if (SUCCEEDED(dev->GetBackBuffer(0, 0, D3DBACKBUFFER_TYPE_MONO, &back))) {
            dev->SetRenderTarget(0, back);
            back->Release();
        }
        target->Release();
    }
    if (targetTexture) targetTexture->Release();
    tex->Release();
    vb->Release();
}

TEST(d3d9_scene_reentrancy, "d3d9.inside-the-games-own-scene") {
    Env env;
    RequireDevice(env, 160, 90);
    FontRegistry fonts;
    GdiGlyphSource glyphs(fonts);
    Renderer renderer(glyphs);
    D3D9Backend backend;
    backend.Attach(env.device);
    SceneError e;
    Scene s;
    CHECK(ParseScene("{\"v\":1,\"w\":160,\"h\":90,\"cmds\":[{\"t\":\"rect\",\"x\":0,\"y\":0,\"w\":160,\"h\":90,\"fill\":\"#ff0000\"}]}", s, e));
    renderer.SetScene(std::make_shared<const Scene>(s));
    Clear(env.device, 0xFF000000u);
    // The game is inside BeginScene when it calls us: we must neither fail nor close its scene.
    CHECK(SUCCEEDED(env.device->BeginScene()));
    CHECK(renderer.Render(backend) == RenderResult::Drawn);
    CHECK(SUCCEEDED(env.device->EndScene()));
    // Outside a scene, the next frame works too, and leaves no scene open.
    CHECK(renderer.Render(backend) == RenderResult::Drawn);
    CHECK(FAILED(env.device->EndScene()));
    int w, h;
    std::vector<uint8_t> px;
    std::string why;
    if (!ReadBack(env.device, w, h, px, why)) SKIP(why);
    CHECK(Near(At(px, w, 80, 45), 255, 0, 0, 0));
    backend.Detach();
}

TEST(d3d9_reset, "d3d9.default-pool-is-released-for-reset") {
    Env env;
    RequireDevice(env, 200, 120);
    FontRegistry fonts;
    GdiGlyphSource glyphs(fonts);
    RegisterTestFonts(fonts);
    Renderer renderer(glyphs);
    renderer.PutImage("player", 2, 2, std::vector<uint8_t>(kPlayerImage));
    D3D9Backend backend;
    backend.Attach(env.device);
    SceneError e;
    Scene s;
    CHECK(ParseScene("{\"v\":1,\"w\":200,\"h\":120,\"background\":true,\"cmds\":[{\"t\":\"image\",\"id\":\"player\",\"x\":10,\"y\":10,\"w\":40,\"h\":40},"
                     "{\"t\":\"text\",\"x\":60,\"y\":20,\"s\":\"Reset\",\"size\":24}]}", s, e));
    renderer.SetScene(std::make_shared<const Scene>(s));
    Clear(env.device, 0xFF204060u);
    CHECK(backend.CaptureBackground() && backend.HasBackground());
    CHECK(renderer.Render(backend) == RenderResult::Drawn);   // creates the dynamic buffers, the atlas and the image
    // With the objects still alive, Reset must be refused (this is what the Reset hook prevents).
    HRESULT hr = env.device->Reset(&env.pp);
    CHECK_MSG(hr == D3DERR_INVALIDCALL, "Reset with default-pool objects alive returned 0x" + std::to_string(static_cast<unsigned long>(hr)));
    backend.ReleaseDefaultPool();
    CHECK(!backend.HasBackground());
    hr = env.device->Reset(&env.pp);
    CHECK_MSG(SUCCEEDED(hr), "Reset after ReleaseDefaultPool failed");
    // Drawing resumes: buffers are recreated, the managed image and glyphs survived untouched.
    Clear(env.device, 0xFF204060u);
    CHECK(renderer.Render(backend) == RenderResult::Drawn);
    int w, h;
    std::vector<uint8_t> px;
    std::string why;
    if (!ReadBack(env.device, w, h, px, why)) SKIP(why);
    CHECK(At(px, w, 14, 14).r > 230 && At(px, w, 14, 14).g < 40);       // the red texel of the image
    CHECK(At(px, w, 46, 46).r > 230 && At(px, w, 46, 46).g > 230);      // the white texel
    int textPixels = 0;
    for (int y = 15; y < 50; ++y) {
        for (int x = 60; x < 150; ++x) {
            const Rgba c = At(px, w, x, y);
            if (c.r > 200 && c.g > 200 && c.b > 200) ++textPixels;
        }
    }
    CHECK_MSG(textPixels > 60, std::to_string(textPixels));
    backend.Detach();
}

TEST(d3d9_layer, "d3d9.layer-through-the-present-and-reset-hooks") {
    Env env, second;
    RequireDevice(env, 480, 270);
    FakeBridge fake;
    fake.gameDevice = env.device;
    DrawLayer layer;
    CHECK(layer.Start(&fake.table) == 0 && layer.HooksInstalled());
    IDirect3DDevice9* dev = env.device;
    const auto has = [](const std::string& text, const char* needle) { return text.find(needle) != std::string::npos; };
    const auto read = [&](std::vector<uint8_t>& px, int& w, int& h) {
        std::string why;
        if (!ReadBack(dev, w, h, px, why)) SKIP(why);
    };
    const std::wstring arial = WindowsFont(L"arial.ttf");
    if (!arial.empty()) {
        std::string path;
        for (const wchar_t c : arial) {
            if (c == L'\\') path += "\\\\";
            else path.push_back(static_cast<char>(c));
        }
        CHECK(has(Run(layer, "font.put", "{\"role\":\"bold\",\"path\":\"" + path + "\"}"), "\"ok\":true"));
    }
    std::vector<uint8_t> px;
    int w = 0, h = 0;

    // The latest scene is drawn at every Present, on the game's device, before the original continues.
    CHECK(has(Run(layer, "scene", Vector("scene-minimal.json")), "\"ok\":true"));
    Clear(dev, 0xFF808080u);
    CHECK(fake.CallPresent(dev) == S_OK && FakeBridge::presentCalls == 1);
    read(px, w, h);
    Rgba inside = At(px, w, 40, 20);   // canvas (160,80): inside the 300x120 panel (a quarter of its size here)
    CHECK_MSG(Near(inside, 36, 39, 43, 3), Describe(inside));
    CHECK(Near(At(px, w, 1, 1), 128, 128, 128, 0));

    // visible:false stops drawing at once.
    Run(layer, "visible", "{\"on\":false}");
    Clear(dev, 0xFF808080u);
    CHECK(fake.CallPresent(dev) == S_OK);
    read(px, w, h);
    CHECK(Near(At(px, w, 40, 20), 128, 128, 128, 0));
    Run(layer, "visible", "{\"on\":true}");
    Clear(dev, 0xFF808080u);
    fake.CallPresent(dev);
    read(px, w, h);
    CHECK(Near(At(px, w, 40, 20), 36, 39, 43, 3));

    // The pause background: captured from the frame the game rendered, redrawn on every Present.
    CHECK(has(Run(layer, "scene", Vector("scene-pause.json")), "\"ok\":true"));
    Clear(dev, 0xFFC80000u);
    CHECK(has(Run(layer, "background.capture", "{}"), "\"pending\":true"));
    fake.CallPresent(dev);
    CHECK(has(Run(layer, "stats", "{}"), "\"captures\":1"));
    read(px, w, h);
    Rgba dim = At(px, w, 25, 25);   // canvas (100,100)
    CHECK_MSG(Near(dim, 70, 0, 0, 3), Describe(dim));
    CHECK(Near(At(px, w, 200, 110), 0, 201, 74, 3));   // the button
    Clear(dev, 0xFF00FF00u);        // the game no longer repaints: DISCARD leaves nothing to rely on
    fake.CallPresent(dev);
    read(px, w, h);
    dim = At(px, w, 25, 25);
    CHECK_MSG(Near(dim, 70, 0, 0, 3), Describe(dim));

    // Reset: the hook releases the default pool before the original runs (otherwise Reset is refused).
    FakeBridge::resetPerforms = dev;
    const long reset = fake.CallReset(dev, &env.pp);
    CHECK_MSG(SUCCEEDED(reset) && FakeBridge::resetCalls == 1, "Reset through the hook failed: " + std::to_string(reset));
    CHECK(has(Run(layer, "stats", "{}"), "\"device_resets\":1"));
    Clear(dev, 0xFF00FF00u);
    fake.CallPresent(dev);   // the background died with the Reset: the scene sits on what the surface holds
    read(px, w, h);
    CHECK(Near(At(px, w, 25, 25), 0, 89, 0, 3));
    Clear(dev, 0xFFC80000u);
    Run(layer, "background.capture", "{}");
    fake.CallPresent(dev);
    CHECK(has(Run(layer, "stats", "{}"), "\"captures\":2"));
    // background.release frees it again.
    Clear(dev, 0xFF00FF00u);
    Run(layer, "background.release", "{}");
    fake.CallPresent(dev);
    read(px, w, h);
    CHECK(Near(At(px, w, 25, 25), 0, 89, 0, 3));
    FakeBridge::resetPerforms = nullptr;

    // Another Direct3D device (the vehicle selection form creates one) is never drawn on.
    if (second.Create(480, 270)) {
        Clear(second.device, 0xFF808080u);
        fake.CallPresent(second.device);
        std::vector<uint8_t> other;
        std::string why;
        int ow = 0, oh = 0;
        if (ReadBack(second.device, ow, oh, other, why)) CHECK(Near(At(other, ow, 25, 25), 128, 128, 128, 0));
        Clear(second.device, 0xFF808080u);
        const long resetOther = fake.CallReset(second.device, &second.pp);
        CHECK(resetOther == S_OK);   // the other device's Reset passes through untouched (the fake original does not reset)
    }

    // Stop: inert at once, the original is still called, nothing is drawn.
    layer.Stop();
    Clear(dev, 0xFF808080u);
    const int before = FakeBridge::presentCalls;
    CHECK(fake.CallPresent(dev) == S_OK && FakeBridge::presentCalls == before + 1);
    read(px, w, h);
    CHECK(Near(At(px, w, 40, 20), 128, 128, 128, 0));
}
