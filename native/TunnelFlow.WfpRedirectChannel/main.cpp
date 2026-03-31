#include <iostream>
#include <string>
#include <vector>

namespace
{
    constexpr char kSeparator = '|';

    std::vector<std::string> Split(const std::string& value)
    {
        std::vector<std::string> parts;
        std::string current;

        for (char ch : value)
        {
            if (ch == kSeparator)
            {
                parts.push_back(current);
                current.clear();
                continue;
            }

            current.push_back(ch);
        }

        parts.push_back(current);
        return parts;
    }
}

int main()
{
    std::ios::sync_with_stdio(false);
    std::string line;

    while (std::getline(std::cin, line))
    {
        if (line == "STOP")
        {
            return 0;
        }

        auto parts = Split(line);
        if (parts.empty())
        {
            continue;
        }

        if (parts[0] == "EMIT" && parts.size() == 13)
        {
            parts[0] = "EVENT";
            for (size_t i = 0; i < parts.size(); ++i)
            {
                if (i > 0)
                {
                    std::cout << kSeparator;
                }

                std::cout << parts[i];
            }

            std::cout << std::endl;
            std::cout.flush();
        }
    }

    return 0;
}
