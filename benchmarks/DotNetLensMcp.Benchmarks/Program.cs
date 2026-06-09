using BenchmarkDotNet.Running;
using DotNetLensMcp.Benchmarks;

BenchmarkRunner.Run<RoslynBenchmarks>(args: args);
