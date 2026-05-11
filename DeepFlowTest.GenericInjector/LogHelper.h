#pragma once

#include "pch.h"

#include <fstream>

class LogHelper
{
private:
	static std::wstring logFile;

public:
	static void SetLogFile(const std::wstring& file)
	{
		logFile = file;
	}

	template<typename ... Args>
	static void WriteLine(const std::wstring& format, Args ... args)
	{
		const auto output = string_format(format, args...);
		OutputDebugString(output.c_str());

		if (!logFile.empty())
		{
			std::wofstream out(logFile, std::ios_base::app);
			out << output << std::endl;
		}
	}
};
