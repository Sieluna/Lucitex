<div align="center">

# Lucitex

*Ex tenuī filo lucis texor*

**A pure C# image & texture library — decode, encode, and convert between formats.**

[![Test](https://github.com/Sieluna/Lucitex/actions/workflows/test.yml/badge.svg)](https://github.com/Sieluna/Lucitex/actions/workflows/test.yml)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Live demo](https://img.shields.io/badge/demo-live-brightgreen)](https://sieluna.github.io/Lucitex/)

</div>

---

### Quick start

```csharp
using var sourceStream = File.OpenRead("input.exr");
var sourceCodec = new ExrCodec();
var reader = sourceCodec.OpenReader(sourceStream);

var targetCodec = new PngCodec();
var plan = ConversionPlanner.Plan(reader.Describe(), targetCodec.Capabilities, ConversionPolicy.Preview).Plan!;

using var targetStream = File.Create("output.png");
var writer = targetCodec.CreateWriter(targetStream, plan.TargetDescriptor);
ConversionExecutor.Execute(plan, reader, sourceCodec.Capabilities.SampleByteOrder, writer, targetCodec.Capabilities.SampleByteOrder);
```

A runnable version of this lives in `examples/Lucitex.Example`.
