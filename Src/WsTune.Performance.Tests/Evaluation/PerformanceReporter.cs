namespace WsTune.Performance.Tests.Evaluation;

public static class PerformanceReporter
{
    public static void Write(ITestOutputHelper output, TransferEvaluation evaluation)
    {
        foreach (var line in evaluation.ToReportLines())
            output.WriteLine(line);
    }

    public static void WriteComparison(ITestOutputHelper output, ComparisonInsight insight)
    {
        Write(output, insight.Direct);
        output.WriteLine(string.Empty);
        Write(output, insight.Tunnel);
        output.WriteLine(string.Empty);
        foreach (var line in insight.ToReportLines())
            output.WriteLine(line);
    }
}
