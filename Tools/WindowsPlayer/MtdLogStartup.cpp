#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shellapi.h>
#include "UnityPlayerStub/Exports.h"

namespace
{
    void ConfigureLog()
    {
        int count = 0;
        LPWSTR* args = CommandLineToArgvW(GetCommandLineW(), &count);
        if (!args) return;
        bool explicitLog = false;
        for (int i = 1; i < count; ++i)
        {
            if (lstrcmpiW(args[i], L"-logFile") == 0)
            {
                explicitLog = true;
                break;
            }
        }
        LocalFree(args);
        // Unity checks this variable before its command line when the engine DLL loads.
        if (explicitLog)
        {
            SetEnvironmentVariableW(L"UNITY_LOG_FILE", nullptr);
            return;
        }
        CreateDirectoryW(LR"(C:\tcs)", nullptr);
        CreateDirectoryW(LR"(C:\tcs\mtd)", nullptr);
        CreateDirectoryW(LR"(C:\tcs\mtd\logs)", nullptr);
        SetEnvironmentVariableW(L"UNITY_LOG_FILE", LR"(C:\tcs\mtd\logs\Player.log)");
    }

    int StartMtdPlayer(HINSTANCE instance, LPCWSTR dataFolder, LPWSTR commandLine, int show)
    {
        ConfigureLog();
        HMODULE engine = LoadLibraryExW(L"UnityPlayer.dll", nullptr,
            LOAD_LIBRARY_SEARCH_APPLICATION_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
        if (!engine) return static_cast<int>(GetLastError());
        auto unityMain = reinterpret_cast<decltype(&UnityMain2)>(GetProcAddress(engine, "UnityMain2"));
        if (!unityMain) return static_cast<int>(GetLastError());
        return unityMain(instance, dataFolder, commandLine, show);
    }
}

// Retain this Editor version's wWinMain and GPU exports. Defer the engine DLL load
// until the process-local environment for its native logging has been set.
#define UnityMain2 StartMtdPlayer
#include "WindowsPlayer/Main.cpp"
