#pragma once

#include "pch.h"
#include "LogHelper.h"

class FrameworkExecutor
{
public:
	explicit FrameworkExecutor(const std::wstring& executorName)
		: name(executorName)
	{
		logPrefix = name + L": ";
	}

	FrameworkExecutor(const FrameworkExecutor&) = delete;
	virtual ~FrameworkExecutor() = default;
	virtual int Execute(LPCWSTR assemblyPath, LPCWSTR typeName, LPCWSTR methodName, LPCWSTR argument, DWORD* returnValue) = 0;

protected:
	template<typename ... Args>
	void Log(const std::wstring& format, Args ... args)
	{
		LogHelper::WriteLine(logPrefix + format, args...);
	}

	std::wstring name;
	std::wstring logPrefix;
};
