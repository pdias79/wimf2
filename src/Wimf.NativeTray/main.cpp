#define UNICODE
#define _UNICODE
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shellapi.h>
#include <shlobj.h>
#include <bcrypt.h>
#include <string>
#include <vector>

#pragma comment(lib, "shell32.lib")
#pragma comment(lib, "bcrypt.lib")
#pragma comment(lib, "advapi32.lib")
#pragma comment(lib, "user32.lib")
#pragma comment(lib, "gdi32.lib")

#define SERVER_RESOURCE 101
#define WM_TRAY (WM_APP + 1)
#define CMD_OPEN 1001
#define CMD_LOGS 1002
#define CMD_ABOUT 1003
#define CMD_EXIT 1004
#define APP_ICON 102
#define CMD_GITHUB 1005
#define WIMF_VERSION L"2.0.0"

static HWND windowHandle = nullptr;
static HWND aboutHandle = nullptr;
static HINSTANCE applicationInstance = nullptr;
static NOTIFYICONDATAW tray = {};
static HANDLE child = nullptr, childThread = nullptr, childInput = nullptr, mutexHandle = nullptr;
static std::wstring logsPath;
static bool stopping = false;
static constexpr wchar_t CLASS_NAME[] = L"Wimf2LightweightTray";
static constexpr wchar_t SERVER_URL[] = L"http://127.0.0.1:5080/";

static void Failure(const wchar_t* message) {
    MessageBoxW(nullptr, message, L"Wimf 2 - lanceur natif", MB_ICONERROR | MB_OK);
}
static std::wstring DirectoryOf(const std::wstring& path) {
    size_t pos = path.find_last_of(L"\\/");
    return pos == std::wstring::npos ? L"." : path.substr(0, pos);
}
static std::wstring ExecutableDirectory() {
    std::vector<wchar_t> buffer(32768);
    DWORD length = GetModuleFileNameW(nullptr, buffer.data(), (DWORD)buffer.size());
    return length && length < buffer.size() ? DirectoryOf(std::wstring(buffer.data(), length)) : L".";
}
static bool Exists(const std::wstring& path) {
    return GetFileAttributesW(path.c_str()) != INVALID_FILE_ATTRIBUTES;
}
static std::wstring Quote(const std::wstring& text) {
    std::wstring value = L"\"";
    for (wchar_t c : text) {
        if (c == L'"') value += L"\\\"";
        else value += c;
    }
    return value + L"\"";
}
static std::wstring HexDigest(const BYTE* buffer, DWORD length) {
    BCRYPT_ALG_HANDLE algorithm = nullptr;
    BCRYPT_HASH_HANDLE hash = nullptr;
    BYTE digest[32] = {};
    if (!BCRYPT_SUCCESS(BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0))) return L"";
    NTSTATUS status = BCryptCreateHash(algorithm, &hash, nullptr, 0, nullptr, 0, 0);
    if (BCRYPT_SUCCESS(status)) status = BCryptHashData(hash, const_cast<PUCHAR>(buffer), length, 0);
    if (BCRYPT_SUCCESS(status)) status = BCryptFinishHash(hash, digest, sizeof(digest), 0);
    if (hash) BCryptDestroyHash(hash);
    BCryptCloseAlgorithmProvider(algorithm, 0);
    if (!BCRYPT_SUCCESS(status)) return L"";
    static constexpr wchar_t digits[] = L"0123456789ABCDEF";
    std::wstring result;
    for (auto c : digest) {
        result += digits[c >> 4];
        result += digits[c & 15];
    }
    return result;
}
static bool WriteAll(HANDLE file, const BYTE* data, DWORD length) {
    while (length) {
        DWORD written = 0;
        if (!WriteFile(file, data, length, &written, nullptr) || !written) return false;
        data += written;
        length -= written;
    }
    return true;
}
static bool ExtractServer(std::wstring& output) {
    HRSRC resource = FindResourceW(nullptr, MAKEINTRESOURCEW(SERVER_RESOURCE), RT_RCDATA);
    if (!resource) return false;
    HGLOBAL loaded = LoadResource(nullptr, resource);
    if (!loaded) return false;
    auto data = static_cast<const BYTE*>(LockResource(loaded));
    DWORD size = SizeofResource(nullptr, resource);
    if (!data || !size) return false;
    auto digest = HexDigest(data, size);
    if (digest.empty()) return false;

    wchar_t local[MAX_PATH] = {};
    if (FAILED(SHGetFolderPathW(nullptr, CSIDL_LOCAL_APPDATA, nullptr, SHGFP_TYPE_CURRENT, local))) return false;
    std::wstring root = std::wstring(local) + L"\\Wimf2";
    CreateDirectoryW(root.c_str(), nullptr);
    root += L"\\server-runtime";
    CreateDirectoryW(root.c_str(), nullptr);
    root += L"\\" + digest;
    CreateDirectoryW(root.c_str(), nullptr);
    output = root + L"\\Wimf.Server.exe";
    if (Exists(output)) return true;

    std::wstring staging = root + L"\\server-" + std::to_wstring(GetCurrentProcessId()) + L".tmp";
    HANDLE file = CreateFileW(staging.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return false;
    bool saved = WriteAll(file, data, size) && FlushFileBuffers(file);
    CloseHandle(file);
    if (saved) saved = MoveFileExW(staging.c_str(), output.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH);
    if (!saved) DeleteFileW(staging.c_str());
    return saved;
}
static std::wstring Arguments() {
    int argc = 0;
    LPWSTR* argv = CommandLineToArgvW(GetCommandLineW(), &argc);
    std::wstring result;
    bool hasConfig = false;
    if (argv) {
        for (int i = 1; i < argc; ++i) {
            if (wcscmp(argv[i], L"--config") == 0) hasConfig = true;
            result += L" " + Quote(argv[i]);
        }
        LocalFree(argv);
    }
    auto configured = ExecutableDirectory() + L"\\wimf.json";
    if (!hasConfig && Exists(configured)) result += L" --config " + Quote(configured);
    return result + L" --tray-managed";
}
static bool StartServer() {
    std::wstring server;
    if (!ExtractServer(server)) return false;
    wchar_t local[MAX_PATH] = {};
    if (FAILED(SHGetFolderPathW(nullptr, CSIDL_LOCAL_APPDATA, nullptr, SHGFP_TYPE_CURRENT, local))) return false;
    std::wstring logFolder = std::wstring(local) + L"\\Wimf2\\logs";
    CreateDirectoryW(logFolder.c_str(), nullptr);
    logsPath = logFolder + L"\\server-native.log";

    SECURITY_ATTRIBUTES sa = {sizeof(sa), nullptr, TRUE};
    HANDLE inputRead = nullptr, inputWrite = nullptr;
    if (!CreatePipe(&inputRead, &inputWrite, &sa, 0)) return false;
    SetHandleInformation(inputWrite, HANDLE_FLAG_INHERIT, 0);
    HANDLE log = CreateFileW(logsPath.c_str(), FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE,
                             &sa, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (log == INVALID_HANDLE_VALUE) {
        CloseHandle(inputRead); CloseHandle(inputWrite); return false;
    }
    std::wstring command = Quote(server) + Arguments();
    std::vector<wchar_t> cmd(command.begin(), command.end());
    cmd.push_back(L'\0');
    STARTUPINFOW startup = {};
    startup.cb = sizeof(startup);
    startup.dwFlags = STARTF_USESTDHANDLES;
    startup.hStdInput = inputRead;
    startup.hStdOutput = log;
    startup.hStdError = log;
    PROCESS_INFORMATION info = {};
    bool started = CreateProcessW(server.c_str(), cmd.data(), nullptr, nullptr, TRUE,
                                  CREATE_NO_WINDOW, nullptr, ExecutableDirectory().c_str(), &startup, &info);
    CloseHandle(inputRead);
    CloseHandle(log);
    if (!started) { CloseHandle(inputWrite); return false; }
    child = info.hProcess;
    childThread = info.hThread;
    childInput = inputWrite;
    return true;
}
static void StopServer() {
    if (stopping) return;
    stopping = true;
    if (child) {
        if (WaitForSingleObject(child, 0) == WAIT_TIMEOUT) {
            DWORD sent = 0;
            if (childInput) WriteFile(childInput, "quit\n", 5, &sent, nullptr);
            if (WaitForSingleObject(child, 8000) == WAIT_TIMEOUT) TerminateProcess(child, 1);
        }
        CloseHandle(child);
        child = nullptr;
    }
    if (childInput) { CloseHandle(childInput); childInput = nullptr; }
    if (childThread) { CloseHandle(childThread); childThread = nullptr; }
}
static LRESULT CALLBACK AboutProcedure(HWND window, UINT message, WPARAM wp, LPARAM lp) {
    if (message == WM_COMMAND && LOWORD(wp) == CMD_GITHUB && HIWORD(wp) == STN_CLICKED) {
        ShellExecuteW(window, L"open", L"https://github.com/pdias79/wimf2", nullptr, nullptr, SW_SHOWNORMAL);
        return 0;
    }
    if (message == WM_CLOSE) { DestroyWindow(window); return 0; }
    if (message == WM_DESTROY) { aboutHandle = nullptr; return 0; }
    return DefWindowProcW(window, message, wp, lp);
}
static void OpenAbout() {
    if (aboutHandle) { ShowWindow(aboutHandle, SW_SHOW); SetForegroundWindow(aboutHandle); return; }
    static bool registered = false;
    if (!registered) {
        WNDCLASSW wc = {};
        wc.lpfnWndProc = AboutProcedure;
        wc.hInstance = applicationInstance;
        wc.lpszClassName = L"Wimf2NativeAbout";
        wc.hIcon = LoadIconW(applicationInstance, MAKEINTRESOURCEW(APP_ICON));
        wc.hCursor = LoadCursorW(nullptr, IDC_ARROW);
        wc.hbrBackground = reinterpret_cast<HBRUSH>(COLOR_WINDOW + 1);
        if (!RegisterClassW(&wc) && GetLastError() != ERROR_CLASS_ALREADY_EXISTS) return;
        registered = true;
    }
    aboutHandle = CreateWindowExW(WS_EX_DLGMODALFRAME, L"Wimf2NativeAbout",
        L"\u00c0 propos de Wimf 2", WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU,
        CW_USEDEFAULT, CW_USEDEFAULT, 440, 280, nullptr, nullptr, applicationInstance, nullptr);
    if (!aboutHandle) return;
    HWND image = CreateWindowW(L"STATIC", nullptr, WS_CHILD | WS_VISIBLE | SS_ICON,
        174, 18, 80, 80, aboutHandle, nullptr, applicationInstance, nullptr);
    HICON icon = static_cast<HICON>(LoadImageW(applicationInstance, MAKEINTRESOURCEW(APP_ICON),
        IMAGE_ICON, 64, 64, LR_DEFAULTCOLOR));
    SendMessageW(image, STM_SETICON, reinterpret_cast<WPARAM>(icon), 0);
    CreateWindowW(L"STATIC", L"Wimf 2 - Version " WIMF_VERSION,
        WS_CHILD | WS_VISIBLE | SS_CENTER, 55, 110, 315, 25,
        aboutHandle, nullptr, applicationInstance, nullptr);
    CreateWindowW(L"STATIC", L"Philippe DIAS", WS_CHILD | WS_VISIBLE | SS_CENTER,
        55, 145, 315, 24, aboutHandle, nullptr, applicationInstance, nullptr);
    HWND link = CreateWindowW(L"STATIC", L"https://github.com/pdias79/wimf2",
        WS_CHILD | WS_VISIBLE | SS_CENTER | SS_NOTIFY,
        55, 183, 315, 27, aboutHandle,
        reinterpret_cast<HMENU>(static_cast<INT_PTR>(CMD_GITHUB)), applicationInstance, nullptr);
    HFONT font = static_cast<HFONT>(GetStockObject(DEFAULT_GUI_FONT));
    SendMessageW(link, WM_SETFONT, reinterpret_cast<WPARAM>(font), TRUE);
    ShowWindow(aboutHandle, SW_SHOW);
    UpdateWindow(aboutHandle);
}

static void Menu() {
    HMENU menu = CreatePopupMenu();
    AppendMenuW(menu, MF_STRING, CMD_OPEN, L"Ouvrir la recherche");
    AppendMenuW(menu, MF_STRING, CMD_LOGS, L"Messages du serveur");
    AppendMenuW(menu, MF_STRING, CMD_ABOUT, L"À propos");
    AppendMenuW(menu, MF_SEPARATOR, 0, nullptr);
    AppendMenuW(menu, MF_STRING, CMD_EXIT, L"Quitter Wimf");
    POINT point = {};
    GetCursorPos(&point);
    SetForegroundWindow(windowHandle);
    TrackPopupMenu(menu, TPM_RIGHTBUTTON, point.x, point.y, 0, windowHandle, nullptr);
    DestroyMenu(menu);
}
static LRESULT CALLBACK WindowProcedure(HWND window, UINT message, WPARAM wp, LPARAM lp) {
    if (message == WM_TRAY) {
        if (LOWORD(lp) == WM_LBUTTONUP) ShellExecuteW(window, L"open", SERVER_URL, nullptr, nullptr, SW_SHOWNORMAL);
        if (LOWORD(lp) == WM_RBUTTONUP || LOWORD(lp) == WM_CONTEXTMENU) Menu();
        return 0;
    }
    if (message == WM_COMMAND) {
        switch (LOWORD(wp)) {
            case CMD_OPEN: ShellExecuteW(window, L"open", SERVER_URL, nullptr, nullptr, SW_SHOWNORMAL); break;
            case CMD_LOGS: ShellExecuteW(window, L"open", L"notepad.exe", Quote(logsPath).c_str(), nullptr, SW_SHOWNORMAL); break;
            case CMD_ABOUT: OpenAbout(); break;
            case CMD_EXIT: DestroyWindow(window); break;
        }
        return 0;
    }
    if (message == WM_DESTROY) {
        Shell_NotifyIconW(NIM_DELETE, &tray);
        StopServer();
        PostQuitMessage(0);
        return 0;
    }
    return DefWindowProcW(window, message, wp, lp);
}
int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR, int) {
    applicationInstance = instance;
    // Same instance name as the .NET tray: do not run the two variants concurrently.
    mutexHandle = CreateMutexW(nullptr, TRUE, L"Local\\Wimf2.Tray.Instance");
    if (!mutexHandle || GetLastError() == ERROR_ALREADY_EXISTS) {
        Failure(L"Wimf 2 est déjà lancé dans la zone de notification.");
        if (mutexHandle) CloseHandle(mutexHandle);
        return 1;
    }
    WNDCLASSW wc = {};
    wc.lpfnWndProc = WindowProcedure;
    wc.hIcon = LoadIconW(instance, MAKEINTRESOURCEW(APP_ICON));
    wc.hInstance = instance;
    wc.lpszClassName = CLASS_NAME;
    RegisterClassW(&wc);
    windowHandle = CreateWindowExW(0, CLASS_NAME, L"Wimf 2", 0, 0, 0, 0, 0, HWND_MESSAGE, nullptr, instance, nullptr);
    if (!windowHandle) { Failure(L"Impossible de créer la fenêtre de messages."); return 2; }
    tray.cbSize = sizeof(tray);
    tray.hWnd = windowHandle;
    tray.uID = 1;
    tray.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
    tray.uCallbackMessage = WM_TRAY;
    tray.hIcon = LoadIconW(instance, MAKEINTRESOURCEW(APP_ICON));
    wcscpy_s(tray.szTip, L"Wimf 2 - Recherche de fichiers");
    if (!Shell_NotifyIconW(NIM_ADD, &tray)) { Failure(L"Impossible d'afficher l'icône de notification."); DestroyWindow(windowHandle); return 3; }
    tray.uVersion = NOTIFYICON_VERSION_4;
    Shell_NotifyIconW(NIM_SETVERSION, &tray);
    if (!StartServer()) {
        Failure(L"Impossible d'extraire ou de démarrer le serveur WIMF. Vérifiez les droits d'accès.");
        DestroyWindow(windowHandle);
        return 4;
    }
    MSG msg = {};
    while (GetMessageW(&msg, nullptr, 0, 0) > 0) { TranslateMessage(&msg); DispatchMessageW(&msg); }
    if (mutexHandle) { ReleaseMutex(mutexHandle); CloseHandle(mutexHandle); }
    return (int)msg.wParam;
}