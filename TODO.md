# Export Files Structure Improvements

## Tasks
- [ ] Change base folder from "logs" to "Reports" across all exporters
- [ ] Improve file naming: Use device name and readable timestamp format
- [ ] Add summary/index sheet to Excel exports
- [ ] Use structured JSON export instead of direct serialization
- [ ] Improve CSV formatting with better headers and organization
- [ ] Ensure consistent naming across all export formats

## Files to Edit
- [ ] ExcelWriter.cs
- [ ] JsonWriter.cs
- [ ] CsvWriter.cs
- [ ] DotExporter.cs

## Testing
- [ ] Test exports to ensure they work correctly
- [ ] Verify folder structure is intuitive
