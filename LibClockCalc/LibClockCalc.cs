namespace LibClockCalc
{
    public enum OptimizationStrategy
    {
        MinimizeDelta,  // Prioritize lowest error to match target sample rate exactly
        PreferHighClock,  // Prefer highest possible FadcKerCk while staying within sample rate tolerance
        Balanced  // A mix of both
    }

    public static class ADCClockGenerator
    {
        // STM32H7 Typical Input Clocks
        private static readonly double[] PossibleHSE_HSI = { 8_000_000, 16_000_000, 25_000_000 }; // HSE/HSI options

        // PLL N (Multiplier) Values
        private static readonly int[] PLLN = Enumerable.Range(50, 201).ToArray(); // 50 to 250

        // PLL Dividers
        private static readonly int[] PLLM = { 1, 2, 3, 4, 5, 6, 8, 10 }; // Known dividers
        private static readonly int[] PLLP = { 2, 4, 6, 8, 10, 12 }; // PLL2_P output dividers
        private static readonly int[] PLLR = { 2, 4, 6, 8 }; // PLL3_R output dividers

        // ADC Prescalers (before the MUX)
        private static readonly int[] ADC_ker_ck_Prescalers = { 1, 2, 4, 6, 8, 10, 12, 16, 32, 64, 128, 256 };
        private static readonly int[] ADC_sclk_Prescalers = { 1, 2, 4 }; // Separate path

        /// <summary>
        /// Generates all valid Fadc_ker_ck values based on STM32H7 PLL settings.
        /// The CubeMX value (Fadc_ker_ck) is AFTER the fixed /2 block.
        /// ADC prescalers are applied BEFORE the MUX.
        /// </summary>
        /// <returns>List of valid ADC clock frequencies (Hz)</returns>
        public static double[] GenerateValidADCClocks()
        {
            HashSet<double> validClocks = new();

            // Iterate through all possible HSE/HSI sources
            foreach (double hse_hsi in PossibleHSE_HSI)
            {
                foreach (int n in PLLN) // Iterate through possible PLL multipliers
                {
                    foreach (int m in PLLM) // Iterate through PLL input dividers
                    {
                        double vco = (hse_hsi * n) / m;

                        foreach (int p in PLLP) // PLL2_P output
                        {
                            double pll2_p_freq = vco / p;
                            if (pll2_p_freq <= 160_000_000)
                            {
                                // Apply ADC_KER_CK prescalers (before the MUX)
                                foreach (int prescaler in ADC_ker_ck_Prescalers)
                                {
                                    double adc_ker_ck_input = pll2_p_freq / prescaler;
                                    if (adc_ker_ck_input <= 160_000_000)
                                    {
                                        validClocks.Add(adc_ker_ck_input * 2); // MUX doubles the frequency
                                    }
                                }
                            }
                        }

                        foreach (int r in PLLR) // PLL3_R output
                        {
                            double pll3_r_freq = vco / r;
                            if (pll3_r_freq <= 160_000_000)
                            {
                                // Apply ADC_KER_CK prescalers (before the MUX)
                                foreach (int prescaler in ADC_ker_ck_Prescalers)
                                {
                                    double adc_ker_ck_input = pll3_r_freq / prescaler;
                                    if (adc_ker_ck_input <= 160_000_000)
                                    {
                                        validClocks.Add(adc_ker_ck_input * 2); // MUX doubles the frequency
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // Apply fixed /2 divider to get Fadc_ker_ck (CubeMX value)
            HashSet<double> finalADCClocks = new();
            foreach (double clock in validClocks)
            {
                double cubeMXFreq = clock / 2; // Apply the /2 block
                if (cubeMXFreq <= 80_000_000 && cubeMXFreq >= 1_666_667) // Enforce CubeMX constraints
                    finalADCClocks.Add(cubeMXFreq);
            }

            return finalADCClocks.OrderByDescending(f => f).ToArray();
        }
    }

    public class ADCConfig
    {
        public required double FadcKerCk { get; init; }  // ADC clock frequency in Hz
        public required double Tsmpl { get; init; }  // Sampling time in ADC cycles
        public required double Tconv { get; init; }  // Total conversion time in seconds
        public required double AchievedSampleRate { get; init; }  // Sample rate in Hz

        public override string ToString() =>
            $"Optimal ADC Settings:\n" +
            $"- Fadc_ker_ck: {FadcKerCk / 1_000_000.0} MHz\n" +
            $"- Sampling Time: {Tsmpl} cycles\n" +
            $"- Total Conversion Time: {Tconv * 1_000_000:F2} µs\n" +
            $"- Achieved Sample Rate: {AchievedSampleRate:F0} Hz";
    }

    public static class ADCConfigCalculator
    {
        public static double[] AllowedSamplingTimes { get; set; } = [1.5, 2.5, 8.5, 16.5, 32.5, 64.5, 387.5, 810.5];
        public static double Tsar { get; set; } = 8.5; // Default ADC conversion overhead in cycles
        private static readonly double[] PreferredFadcKerCkValues = ADCClockGenerator.GenerateValidADCClocks();

        /// <summary>
        /// Generates all possible valid ADC configurations.
        /// </summary>
        /// <param name="targetSampleRate">Target sample rate in Hz</param>
        /// <returns>List of all valid ADC configurations</returns>
        private static List<ADCConfig> GetAllValidConfigs(double targetSampleRate)
        {
            return PreferredFadcKerCkValues
                .SelectMany(fadc => AllowedSamplingTimes, (fadc, tsmp) => new ADCConfig
                {
                    FadcKerCk = fadc,
                    Tsmpl = tsmp,
                    Tconv = (tsmp + Tsar) / fadc,
                    AchievedSampleRate = 1 / ((tsmp + Tsar) / fadc)
                })
                .ToList();
        }

        /// <summary>
        /// Finds the best ADC configuration based on the selected optimization strategy.
        /// </summary>
        /// <param name="targetSampleRate">Target sample rate in Hz</param>
        /// <param name="strategy">Strategy to optimize for (MinimizeDelta, PreferHighClock, Balanced)</param>
        /// <returns>Optimal ADC configuration</returns>
        public static ADCConfig FindOptimalSettings(double targetSampleRate, OptimizationStrategy strategy = OptimizationStrategy.Balanced)
        {
            var allConfigs = GetAllValidConfigs(targetSampleRate);
            double bestError = allConfigs.Min(c => Math.Abs(c.AchievedSampleRate - targetSampleRate));

            return strategy switch
            {
                OptimizationStrategy.MinimizeDelta => allConfigs
                    .OrderBy(c => Math.Abs(c.AchievedSampleRate - targetSampleRate))
                    .First(),

                OptimizationStrategy.PreferHighClock => allConfigs
                    .Where(c => Math.Abs(c.AchievedSampleRate - targetSampleRate) <= bestError * 1.5) // Allow some tolerance
                    .OrderByDescending(c => c.FadcKerCk)
                    .First(),

                _ => allConfigs
                    .OrderBy(c => Math.Abs(c.AchievedSampleRate - targetSampleRate))
                    .ThenByDescending(c => c.FadcKerCk)
                    .First()
            };
        }

        /// <summary>
        /// Returns a list of possible configurations instead of just one.
        /// </summary>
        /// <param name="targetSampleRate">Target sample rate in Hz</param>
        /// <param name="maxResults">Maximum number of results to return</param>
        /// <returns>List of ADC configurations</returns>
        public static List<ADCConfig> FindMultipleSettings(double targetSampleRate, int maxResults = 5)
        {
            return GetAllValidConfigs(targetSampleRate)
                .OrderBy(c => Math.Abs(c.AchievedSampleRate - targetSampleRate))
                .ThenByDescending(c => c.FadcKerCk)
                .Take(maxResults)
                .ToList();
        }

        private static double[] GeneratePreferredFrequencies() =>
            Enumerable.Range(1, 80)
                      .Select(f => (double)f)
                      .Where(f => f >= 1 && f <= 80 &&
                                 (f % 10 == 0 || new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 12.5, 25, 37.5, 50, 60, 75 }.Contains(f)))
                      .Select(f => f * 1_000_000) // Convert to Hz
                      .Distinct()
                      .OrderByDescending(f => f) // Sort high-to-low for "PreferHighClock"
                      .ToArray();
    }
}
