using LibClockCalc;

double targetSampleRate = 200_000; // 200 kHz

// Find a Single Best Configuration
/*
ADCConfig optimalConfig = ADCConfigCalculator.FindOptimalSettings(targetSampleRate, OptimizationStrategy.PreferHighClock);
Console.WriteLine(optimalConfig);
*/

// Get Multiple Valid Configurations
List<ADCConfig> configs = ADCConfigCalculator.FindMultipleSettings(targetSampleRate, maxResults: 3);
foreach (var config in configs)
{
    Console.WriteLine(config);
    Console.WriteLine();
}

// Example: Modifying AllowedSamplingTimes & Tsar dynamically
/*
Console.WriteLine("\nTesting with custom settings...");
ADCConfigCalculator.AllowedSamplingTimes = new double[] { 2.5, 8.5, 16.5, 32.5 };
ADCConfigCalculator.Tsar = 10.0;  // Change Tsar to 10 cycles
*/
