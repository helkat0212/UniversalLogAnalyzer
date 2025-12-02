# Contributing to Universal Log Analyzer

Thank you for your interest in contributing to Universal Log Analyzer! This document provides guidelines and instructions for getting involved.

## Code of Conduct

Please be respectful and constructive in all interactions. We're committed to creating a welcoming environment for all contributors.

## Getting Started

### Prerequisites
- .NET SDK 8.0 or higher
- Git
- Visual Studio 2022+ or VS Code with C# extension

### Setting Up Development Environment

1. Fork the repository
2. Clone your fork:
   ```powershell
   git clone https://github.com/YOUR-USERNAME/HEK.git
   cd HEK
   ```
3. Add upstream remote:
   ```powershell
   git remote add upstream https://github.com/vladyslavroshchuk-si231-code/HEK.git
   ```
4. Build the project:
   ```powershell
   dotnet build HuaweiLogAnalyzer.sln -c Release
   ```
5. Run tests:
   ```powershell
   dotnet test HuaweiLogAnalyzer.Tests -c Release
   ```

## How to Contribute

### Reporting Bugs

Before creating a bug report:
- Check existing issues to avoid duplicates
- Verify you're using the latest version

When reporting, include:
- Step-by-step reproduction
- Expected vs actual behavior
- Your environment (Windows version, .NET version, RAM, CPU)
- Sample log file (sanitized of sensitive data)
- Screenshots if applicable

**Example issue**:
```
Title: Memory spike when analyzing 100MB+ files

Environment: Windows 10 Pro, .NET 8.0.1, Core i5, 8GB RAM

Steps to reproduce:
1. Open UniversalLogAnalyzer.exe
2. Select 100MB+ log file
3. Click Analyze

Expected: Smooth processing, memory ~400MB
Actual: Memory jumps to 2GB, system becomes sluggish

Error messages: None (UI just becomes unresponsive)
```

### Suggesting Enhancements

Good enhancement suggestions include:
- Clear use case and benefits
- Examples of how it would work
- Possible implementation approaches
- Links to related features in other tools

**Example suggestion**:
```
Title: Support for Cisco IOS log format

Use case: Many networks use mixed Huawei/Cisco devices, 
would be helpful to analyze both in one tool

Implementation: Create CiscoAnalyzer.cs parallel to Analyzer.cs, 
reuse export infrastructure

Related: Juniper logs (could be future enhancement)
```

### Submitting Code Changes

1. **Create a feature branch**:
   ```powershell
   git checkout -b feature/your-feature-name
   ```

2. **Make your changes** following the coding standards (see below)

3. **Write/update tests** for your changes:
   ```powershell
   dotnet test HuaweiLogAnalyzer.Tests -c Release
   ```

4. **Commit with clear messages**:
   ```powershell
   git commit -m "Add feature: description

   - Detailed explanation of changes
   - Rationale for approach
   - Any breaking changes or gotchas"
   ```

5. **Push to your fork**:
   ```powershell
   git push origin feature/your-feature-name
   ```

6. **Create Pull Request** from GitHub UI:
   - Title: "Add feature: description"
   - Description: Link related issue, explain changes
   - Checklist: Confirm tests pass, no breaking changes

## Coding Standards

### C# Style Guide

```csharp
// 1. Naming conventions
public class InterfaceAnalyzer { }           // PascalCase for classes
public static void AnalyzeLog() { }          // PascalCase for methods
private List<string> _results;               // camelCase with _ for private fields
public const int MaxBufferSize = 1024;       // UPPER_SNAKE for constants

// 2. Formatting
if (condition)
{
    // Allman style braces
}

// 3. Comments for non-obvious code
// NOT: Increment counter
counter++;

// YES: Update utilization cache before exporting
interfaceUtilizations[iface] = newValue;

// 4. Performance considerations
private static readonly Regex _pattern = new Regex(...); // Cache Regex

// 5. Error handling
try
{
    // Do work
}
catch (SpecificException ex)
{
    _logger.LogError($"Specific error: {ex.Message}");
    // Handle or rethrow
}
catch
{
    // Avoid bare catch blocks
}
```

### Testing Standards

Every new feature should include tests:

```csharp
[Fact]
public void AnalyzeFile_WithHighUtilizationInterface_ReturnsHighUtilizationCluster()
{
    // Arrange
    var logContent = @"interface Gi0/0/1
 description High traffic port
 display interface brief | include Gi0/0/1
 Gi0/0/1          UP    UP    null  null
 InUti(%) OutUti(%) InErrors OutErrors
 85.5     92.3       150      75";
    var tempFile = Path.GetTempFileName();
    File.WriteAllText(tempFile, logContent);
    
    // Act
    var result = Analyzer.AnalyzeFile(tempFile);
    
    // Assert
    Assert.Contains(
        result.Clustering.Clusters,
        c => c.ClusterName.Contains("High") && c.Interfaces.Contains("Gi0/0/1")
    );
    
    // Cleanup
    File.Delete(tempFile);
}
```

### Documentation Standards

- Update README for user-facing changes
- Add XML comments for public APIs:
  ```csharp
  /// <summary>
  /// Analyzes log file and detects anomalies
  /// </summary>
  /// <param name="filePath">Path to log file</param>
  /// <returns>Analysis results including anomalies and metrics</returns>
  public static LogData AnalyzeFile(string filePath)
  ```
- Update CHANGELOG.md for releases

## Areas for Contribution

### High Priority
- [ ] Support for additional Huawei VRP versions
- [ ] Performance optimization (streaming exports, memory profiling)
- [ ] Bug fixes and stability improvements
- [ ] Test coverage expansion

### Medium Priority
- [ ] UI improvements (dark theme, better layout)
- [ ] Additional anomaly detection rules
- [ ] Localization (Ukrainian, Russian, Chinese, etc.)
- [ ] Documentation improvements

### Future/Exploratory
- [ ] Avalonia port (Linux/macOS support)
- [ ] Machine learning integration (LSTM for failure prediction)
- [ ] Support for other vendors (Cisco, Juniper, Mikrotik)
- [ ] Web API for enterprise integration
- [ ] Real-time log streaming analysis

## Review Process

1. **Automated Checks**: GitHub Actions runs tests and code analysis
2. **Code Review**: Maintainers review changes for:
   - Code quality and consistency
   - Performance impact
   - Test coverage
   - Documentation completeness
3. **Feedback**: Maintainers may request changes
4. **Merge**: Once approved, changes are merged

## Building and Publishing

For maintainers releasing new versions:

```powershell
# Increment version in .csproj files
# Update CHANGELOG.md
# Create release branch

git checkout -b release/v1.1.0
# Make version changes
git commit -m "Release: v1.1.0"
git tag -a v1.1.0 -m "Version 1.1.0 release"
git push origin release/v1.1.0
git push origin v1.1.0

# GitHub Actions automatically builds and uploads EXE to Releases
```

## Communication

- **Issues**: Use GitHub Issues for bugs and features
- **Discussions**: Use GitHub Discussions for general questions
- **Email**: For sensitive topics, contact maintainers directly

## Recognition

Contributors will be recognized in:
- CHANGELOG.md
- GitHub contributors page
- Acknowledgments section of ScientificThesis.md

## Legal

By contributing, you agree that your contributions will be licensed under the MIT License.

---

Thank you for making HuaweiLogAnalyzer better! 🚀
