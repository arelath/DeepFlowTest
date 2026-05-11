#include "pch.h"
#include "NetFrameworkExecutor.h"

#include "mscoree.h"
#pragma comment(lib, "mscoree.lib")

ICLRRuntimeHost* NetFrameworkExecutor::GetRuntimeHost()
{
	ICLRMetaHost* metaHost = nullptr;
	ICLRRuntimeInfo* runtimeInfo = nullptr;
	ICLRRuntimeHost* runtimeHost = nullptr;

	auto hr = CLRCreateInstance(CLSID_CLRMetaHost, IID_ICLRMetaHost, reinterpret_cast<LPVOID*>(&metaHost));
	if (FAILED(hr) || !metaHost)
	{
		Log(L"CLRCreateInstance failed with HRESULT %i.", hr);
		return nullptr;
	}

	hr = metaHost->GetRuntime(L"v4.0.30319", IID_ICLRRuntimeInfo, reinterpret_cast<LPVOID*>(&runtimeInfo));
	if (SUCCEEDED(hr) && runtimeInfo)
	{
		hr = runtimeInfo->GetInterface(CLSID_CLRRuntimeHost, IID_ICLRRuntimeHost, reinterpret_cast<LPVOID*>(&runtimeHost));
		if (FAILED(hr))
			Log(L"GetInterface failed with HRESULT %i.", hr);
	}
	else
	{
		Log(L"GetRuntime failed with HRESULT %i.", hr);
	}

	if (runtimeInfo)
		runtimeInfo->Release();
	metaHost->Release();

	return runtimeHost;
}

int NetFrameworkExecutor::Execute(LPCWSTR assemblyPath, LPCWSTR typeName, LPCWSTR methodName, LPCWSTR argument, DWORD* returnValue)
{
	auto* runtimeHost = GetRuntimeHost();
	if (!runtimeHost)
		return E_FAIL;

	Log(L"Calling ExecuteInDefaultAppDomain.");
	const auto hr = runtimeHost->ExecuteInDefaultAppDomain(assemblyPath, typeName, methodName, argument, returnValue);
	runtimeHost->Release();
	return hr;
}
