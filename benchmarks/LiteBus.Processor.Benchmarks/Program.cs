using BenchmarkDotNet.Running;
using LiteBus.Processor.Benchmarks;

BenchmarkRunner.Run(typeof(Program).Assembly);
