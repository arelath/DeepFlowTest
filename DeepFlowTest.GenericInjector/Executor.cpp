#include "pch.h"
#include "LogHelper.h"

#include <comdef.h>

#include "NetExecutor.h"
#include "NetFrameworkExecutor.h"

std::unique_ptr<FrameworkExecutor> GetExecutor(const std::wstring& framework)
{
	LogHelper::WriteLine(L"Selecting executor for framework '%s'.", framework.c_str());

	if (icase_cmp(framework, L"netcoreapp") || icase_cmp(framework, L"dotnet"))
		return std::make_unique<NetExecutor>();

	if (icase_cmp(framework, L"netframework"))
		return std::make_unique<NetFrameworkExecutor>();

	LogHelper::WriteLine(L"Framework '%s' is not supported.", framework.c_str());
	return nullptr;
}

extern "C" __declspec(dllexport) int STDMETHODCALLTYPE ExecuteInDefaultAppDomain(const LPCWSTR input)
{
	try
	{
		if (input == nullptr)
		{
			LogHelper::WriteLine(L"Input parameter was null.");
			return E_INVALIDARG;
		}

		const auto parts = split(input, L"<|>");
		if (parts.size() != 6)
		{
			LogHelper::WriteLine(L"Malformed input parameter. Expected 6 parts and received %i.", static_cast<int>(parts.size()));
			return E_INVALIDARG;
		}

		const auto& framework = parts.at(0);
		const auto& assemblyPath = parts.at(1);
		const auto& className = parts.at(2);
		const auto& methodName = parts.at(3);
		const auto& startupArgument = parts.at(4);
		const auto& logFile = parts.at(5);

		LogHelper::SetLogFile(logFile);
		LogHelper::WriteLine(L"Framework: %s", framework.c_str());
		LogHelper::WriteLine(L"Assembly: %s", assemblyPath.c_str());
		LogHelper::WriteLine(L"Type: %s", className.c_str());
		LogHelper::WriteLine(L"Method: %s", methodName.c_str());
		LogHelper::WriteLine(L"Startup argument length: %i", static_cast<int>(startupArgument.size()));

		const auto executor = GetExecutor(framework);
		if (!executor)
			return E_NOTIMPL;

		DWORD returnValue = 0;
		const auto hr = executor->Execute(assemblyPath.c_str(), className.c_str(), methodName.c_str(), startupArgument.c_str(), &returnValue);

		if (FAILED(hr))
		{
			const _com_error err(hr);
			LogHelper::WriteLine(L"ExecuteInDefaultAppDomain failed.");
			LogHelper::WriteLine(L"HResult: %i", hr);
			LogHelper::WriteLine(L"Message: %s", err.ErrorMessage());
			LogHelper::WriteLine(L"Description: %s", std::wstring(err.Description(), SysStringLen(err.Description())).c_str());
		}

		return hr;
	}
	catch (std::exception& exception)
	{
		LogHelper::WriteLine(L"ExecuteInDefaultAppDomain failed with exception.");
		LogHelper::WriteLine(to_wstring(exception.what()));
	}
	catch (...)
	{
		LogHelper::WriteLine(L"ExecuteInDefaultAppDomain failed with unknown exception.");
	}

	return E_FAIL;
}
