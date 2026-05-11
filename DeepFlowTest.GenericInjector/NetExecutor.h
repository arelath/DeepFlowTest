#pragma once

#include "pch.h"
#include "mscoree.h"
#include "FrameworkExecutor.h"

class NetExecutor final : public FrameworkExecutor
{
public:
	NetExecutor()
		: FrameworkExecutor(L"NetExecutor")
	{
	}

	int Execute(LPCWSTR assemblyPath, LPCWSTR typeName, LPCWSTR methodName, LPCWSTR argument, DWORD* returnValue) override;

private:
	ICLRRuntimeHost* GetRuntimeHost();
};
