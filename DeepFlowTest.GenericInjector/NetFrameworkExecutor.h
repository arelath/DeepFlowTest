#pragma once

#include "pch.h"
#include <metahost.h>
#include "FrameworkExecutor.h"

class NetFrameworkExecutor final : public FrameworkExecutor
{
public:
	NetFrameworkExecutor()
		: FrameworkExecutor(L"NetFrameworkExecutor")
	{
	}

	int Execute(LPCWSTR assemblyPath, LPCWSTR typeName, LPCWSTR methodName, LPCWSTR argument, DWORD* returnValue) override;

private:
	ICLRRuntimeHost* GetRuntimeHost();
};
