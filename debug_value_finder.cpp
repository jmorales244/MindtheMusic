#include <iostream>
#include <fstream>
#include <string>
#include <limits>

int main() {
    // Open the file
    std::ifstream inputFile("numbers.txt"); 
    if (!inputFile) {
        std::cerr << "Error: Could not open the file." << std::endl;
        return 1;
    }

    double lowest = std::numeric_limits<double>::max(); // Initialize with maximum possible value
    std::string line;
    std::string targetPhrase = "Video playing in chrome:"; // The specific phrase to search for

    bool foundNumber = false; // Track whether a valid number is found

    // Process each line in the file
    while (std::getline(inputFile, line)) {
        // Check if the line contains the target phrase
        size_t pos = line.find(targetPhrase);
        if (pos != std::string::npos) {
            try {
                // Extract the number after the target phrase
                double number = std::stod(line.substr(pos + targetPhrase.length()));
                foundNumber = true; // A valid number is found
                if (number < lowest) {
                    lowest = number;
                }
            } catch (const std::exception& e) {
                // Handle any invalid conversions
                std::cerr << "Error converting number in line: " << line << std::endl;
            }
        }
    }

    inputFile.close(); // Close the file

    // Output the lowest value
    if (!foundNumber) {
        std::cout << "No valid numbers were found after the target phrase." << std::endl;
    } else {
        std::cout << "The lowest value is: " << lowest << std::endl;
    }

    return 0;
}
