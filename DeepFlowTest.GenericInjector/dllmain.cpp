#include <windows.h>

extern "C" __declspec(dllexport) int DeepFlowTestGenericInjectorVersion()
{
  return 1;
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID reserved)
{
  UNREFERENCED_PARAMETER(module);
  UNREFERENCED_PARAMETER(reason);
  UNREFERENCED_PARAMETER(reserved);
  return TRUE;
}
