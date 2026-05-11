#pragma once

#include <algorithm>
#include <codecvt>
#include <cwctype>
#include <iostream>
#include <locale>
#include <memory>
#include <stdexcept>
#include <string>
#include <vector>

#include "framework.h"

static bool icase_wchar_cmp(const wchar_t left, const wchar_t right)
{
	return towlower(left) == towlower(right);
}

static bool icase_cmp(const std::wstring& left, const std::wstring& right)
{
	return left.size() == right.size() && std::equal(left.begin(), left.end(), right.begin(), icase_wchar_cmp);
}

static std::vector<std::wstring> split(const std::wstring& input, const std::wstring& delimiter)
{
	std::vector<std::wstring> parts;
	std::wstring::size_type startIndex = 0;
	std::wstring::size_type endIndex;

	while ((endIndex = input.find(delimiter, startIndex)) < input.size())
	{
		parts.push_back(input.substr(startIndex, endIndex - startIndex));
		startIndex = endIndex + delimiter.size();
	}

	parts.push_back(input.substr(startIndex));
	return parts;
}

static std::wstring to_wstring(const std::string& input)
{
	std::wstring_convert<std::codecvt<wchar_t, char, std::mbstate_t>> converter;
	return converter.from_bytes(input);
}

template<typename ... Args>
static std::wstring string_format(const std::wstring& format, Args ... args)
{
	if (format.empty())
		return std::wstring();

	const auto size = _scwprintf(format.c_str(), args...);
	if (size <= 0)
		throw std::runtime_error("Error during string formatting.");

	const auto adjustedSize = size + 1;
	const std::unique_ptr<wchar_t[]> buffer(new wchar_t[adjustedSize]);
	swprintf_s(buffer.get(), adjustedSize, format.c_str(), args...);
	return std::wstring(buffer.get(), buffer.get() + adjustedSize - 1);
}
