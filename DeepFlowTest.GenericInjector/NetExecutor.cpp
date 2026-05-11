#include "pch.h"
#include "NetExecutor.h"

typedef HRESULT(STDAPICALLTYPE* FnGetCLRRuntimeHost)(REFIID riid, IUnknown** runtimeHost);

ICLRRuntimeHost* NetExecutor::GetRuntimeHost()
{
	Log(L"Getting coreclr.dll module handle.");
	auto* const coreClrModule = ::GetModuleHandle(L"coreclr.dll");
	if (!coreClrModule)
	{
		Log(L"coreclr.dll is not loaded.");
		return nullptr;
	}

	const auto getRuntimeHost = reinterpret_cast<FnGetCLRRuntimeHost>(::GetProcAddress(coreClrModule, "GetCLRRuntimeHost"));
	if (!getRuntimeHost)
	{
		Log(L"GetCLRRuntimeHost export was not found.");
		return nullptr;
	}

	ICLRRuntimeHost* runtimeHost = nullptr;
	const auto hr = getRuntimeHost(IID_ICLRRuntimeHost, reinterpret_cast<IUnknown**>(&runtimeHost));
	if (FAILED(hr))
	{
		Log(L"GetCLRRuntimeHost failed with HRESULT %i.", hr);
		return nullptr;
	}

	return runtimeHost;
}

int NetExecutor::Execute(LPCWSTR assemblyPath, LPCWSTR typeName, LPCWSTR methodName, LPCWSTR argument, DWORD* returnValue)
{
	auto* runtimeHost = GetRuntimeHost();
	if (!runtimeHost)
		return E_FAIL;

	Log(L"Calling ExecuteInDefaultAppDomain.");
	const auto hr = runtimeHost->ExecuteInDefaultAppDomain(assemblyPath, typeName, methodName, argument, returnValue);
	runtimeHost->Release();
	return hr;
}
