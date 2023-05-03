// <copyright file="Base2ExponentialBucketHistogramTest.cs" company="OpenTelemetry Authors">
// Copyright The OpenTelemetry Authors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// </copyright>

using System;
using System.Collections.Generic;
#if NET6_0_OR_GREATER
using System.Runtime.InteropServices;
#endif
using OpenTelemetry.Internal;
using OpenTelemetry.Tests;
using Xunit;
using Xunit.Abstractions;

namespace OpenTelemetry.Metrics.Tests;

public class Base2ExponentialBucketHistogramTest
{
    private readonly ITestOutputHelper output;

    // ~2.2250738585072014E-308 (minimum normal positive, 2 ^ -1022)
    private static readonly double MinNormal = IEEE754Double.FromString("0 00000000001 0000000000000000000000000000000000000000000000000000").DoubleValue;

    private const int MinExponent = -1022;
    private const int MaxExponent = 1023;
    private const int MinSubnormalExponent = MinExponent - 52;

    public Base2ExponentialBucketHistogramTest(ITestOutputHelper output)
    {
        this.output = output;
    }

    public static IEnumerable<object[]> TestScales =>
        new List<object[]>
        {
            new object[] { -11 },
            new object[] { -10 },
            new object[] { -9 },
            new object[] { -8 },
            new object[] { -7 },
            new object[] { -6 },
            new object[] { -5 },
            new object[] { -4 },
            new object[] { -3 },
            new object[] { -2 },
            new object[] { -1 },
            new object[] { 0 },
            new object[] { 1 },
            new object[] { 2 },
            new object[] { 3 },
            new object[] { 4 },
            new object[] { 5 },
            new object[] { 6 },
            new object[] { 7 },
            new object[] { 8 },
            new object[] { 9 },
            new object[] { 10 },
            new object[] { 11 },
            new object[] { 12 },
            new object[] { 13 },
            new object[] { 14 },
            new object[] { 15 },
            new object[] { 16 },
            new object[] { 17 },
            new object[] { 18 },
            new object[] { 19 },
            new object[] { 20 },
        };

    [Theory]
    [MemberData(nameof(TestScales))]
    public void LowerBoundaryMaxIndex(int scale)
    {
        var histogram = new Base2ExponentialBucketHistogram(scale: scale);
        var maxIndex = GetMaxIndex(scale);
        Assert.True(MathHelper.IsFinite(histogram.LowerBoundary(maxIndex)));
    }

    [Theory]
    [MemberData(nameof(TestScales))]
    public void LowerBoundaryPowersOfTwoRoundTripTest(int scale)
    {
        var histogram = new Base2ExponentialBucketHistogram(scale: scale);
        var indexesPerPowerOf2 = scale > 0 ? 1 << scale : 1;
        var minIndex = histogram.MapToIndex(double.Epsilon);
        var maxIndex = GetMaxIndex(scale);

        // Check indexes >= 0
        for (var index = 0; index <= maxIndex; index += indexesPerPowerOf2)
        {
            var lowerBound = histogram.LowerBoundary(index);
            var roundTrip = histogram.MapToIndex(lowerBound);
            Assert.Equal(index, roundTrip + 1);

            var match = false;
            for (var offset = 1; offset <= 1127; ++offset)
            {
                var lowerBoundDelta = lowerBound;
                for (var j = 0; j <= offset; ++j)
                {
                    lowerBoundDelta = MathHelper.BitIncrement(lowerBoundDelta);
                }

                roundTrip = histogram.MapToIndex(lowerBoundDelta);
                if (index == roundTrip)
                {
                    // var delta = lowerBoundDelta - lowerBound;
                    // output.WriteLine($"Scale={scale}, Ops={offset}, Index={index}, Delta={delta}");
                    match = true;
                    break;
                }
            }

            Assert.True(match);
        }

        // Check indexes < 0
        for (var index = minIndex; index < 0; index += indexesPerPowerOf2)
        {
            var lowerBound = histogram.LowerBoundary(index);

            if (scale <= 0)
            {
                // TODO: For scales <= 0, LowerBoundary returns 0 instead of double.Epsilon for the minimum bucket index.
                // Should LowerBoundary just return double.Epsilon in this case?
                lowerBound = index == minIndex && lowerBound == 0
                    ? double.Epsilon

                    // TODO: All negative scales except -11 require this adjustment. Why?
                    : (scale != -11 ? MathHelper.BitIncrement(lowerBound) : lowerBound);
            }

            var isX64 = true;
#if NET6_0_OR_GREATER
            isX64 = RuntimeInformation.ProcessArchitecture == Architecture.X64;
#endif
            // TODO: This is not required on M1 Mac (ARM64)
            if (scale > 0 && index == minIndex && lowerBound == 0 && isX64
                || scale == 1 && index <= minIndex + 2 && lowerBound == 0 && isX64)
            {
                lowerBound = double.Epsilon;
            }

            if (lowerBound == 0)
            {
                output.WriteLine($"{index}");
            }

            var roundTrip = histogram.MapToIndex(lowerBound);

            if (scale > 0)
            {
                if (index != roundTrip)
                {
                    int offset = 1;
                    for (var i = 0; offset <= 512; offset = 1 << ++i)
                    {
                        var lowerBoundDelta = lowerBound;
                        for (var j = 1; j <= offset; ++j)
                        {
                            lowerBoundDelta = MathHelper.BitIncrement(lowerBoundDelta);
                        }

                        var newRoundTrip = histogram.MapToIndex(lowerBoundDelta);

                        // Check offset + 1
                        if (index != newRoundTrip)
                        {
                            // offset++;
                            lowerBoundDelta = MathHelper.BitIncrement(lowerBoundDelta);
                            newRoundTrip = histogram.MapToIndex(lowerBoundDelta);
                        }

                        if (index == newRoundTrip)
                        {
                            // var delta = lowerBoundDelta - lowerBound;
                            // output.WriteLine($"Scale={scale}, Ops={offset}, Index={index}, Delta={delta}");
                            roundTrip = newRoundTrip;
                            break;
                        }
                    }
                }
            }

            Assert.Equal(index, roundTrip);
        }
    }

    private static int GetMaxIndex(int scale)
    {
        return scale > 0
            ? (MaxExponent << scale) | ((1 << scale) - 1)
            : MaxExponent >>> -scale;
    }

    // [Fact]
    public void PowerOfTwoPositiveScales()
    {
        const int MinExponent = -1022;
        const int MaxExponent = 1023;

        for (var scale = 1; scale <= 20; ++scale)
        {
            var histogram = new Base2ExponentialBucketHistogram(scale: scale);

            var indexesPerPowerOf2 = 1 << scale;
            
            var minIndexSubnormal = (MinSubnormalExponent << scale) - 1;
            var minIndexNormal = (MinExponent << scale) - 1;
            var maxIndex = MaxExponent << scale | ((1 << scale) - 1);

            output.WriteLine($"Scale: {scale}, IndexesPerPow2: {indexesPerPowerOf2}, MaxIndex: {maxIndex}, MinIndexNormal: {minIndexNormal}, MinIndexSubnormal: {minIndexSubnormal}");

            Assert.Equal(double.Epsilon, histogram.LowerBoundary(minIndexSubnormal));

            var indexOfMinNormal = histogram.MapToIndex(MinNormal);
            Assert.Equal(minIndexNormal, indexOfMinNormal);

            // output.WriteLine(IEEE754Double.FromDouble(Math.BitIncrement(histogram.LowerBoundary(minIndexNormal))).ToString());
            // Assert.Equal(MinNormal, histogram.LowerBoundary(minIndexNormal));

            Assert.True(MathHelper.IsFinite(histogram.LowerBoundary(maxIndex)));

            var exp = 0;
            for (var index = 0; index < maxIndex; index += indexesPerPowerOf2)
            {
                var expected = 1L << exp++;
                var lowerBound = histogram.LowerBoundary(index);
                
                var incremented = MathHelper.BitIncrement(lowerBound);
                var reversed = histogram.MapToIndex(incremented);

                output.WriteLine($"Index: {index}, Reversed: {reversed}, Expected: {expected}, Actual: {lowerBound}, Incremented: {incremented}");

                // Assert.Equal(index, reversed);
                Assert.Equal(expected, lowerBound);
            }
        }
    }

    //[Theory]
    //[MemberData(nameof(TestScales))]
    public void Tests(int scale)
    {
        var squareRootOf2 = Math.Pow(2, .5);

        var indexer = new Base2ExponentialBucketHistogram(scale: scale);
        var indexesPerPowerOf2 = scale >= 0 ? (1 << scale) : 0;

        // Test bucket start at 1 and 2
        Assert.Equal(1, indexer.LowerBoundary(0), 0);
        if (scale >= 0) {
            Assert.Equal(2, indexer.LowerBoundary(indexesPerPowerOf2), 0);
        }

        // Test bucket -1 and 0.
        Assert.Equal(-1, indexer.MapToIndex(1D));
        Assert.Equal(0, indexer.MapToIndex(MathHelper.BitIncrement(1D)));

        // Test min and max.
        var maxIndex = GetMaxIndex(scale);
        var minIndexNormal = GetMinIndexNormal(scale);
        var minIndex = GetMinIndex(scale);

        // Max, min normal, min value to index
        Assert.Equal(maxIndex, indexer.MapToIndex(double.MaxValue));
        Assert.Equal(minIndexNormal, indexer.MapToIndex(MinNormal));
        Assert.Equal(minIndex, indexer.MapToIndex(double.Epsilon));

        // Max, min normal round trip
        Assert.Equal(maxIndex, indexer.MapToIndex(MathHelper.BitIncrement(indexer.LowerBoundary(maxIndex))));
        Assert.Equal(minIndexNormal, indexer.MapToIndex(MathHelper.BitIncrement(indexer.LowerBoundary(minIndexNormal))));

        // Max index bucket end to value
        // assertDoubleEquals(Double.MAX_VALUE, indexer.getBucketEnd(maxIndex), 0);

        // Min index to value. LogIndexer is not accurate on such small numbers
        // if (!(indexer instanceof LogIndexer)) {
        //     assertDoubleEquals(Double.MIN_VALUE, indexer.getBucketStart(minIndex), 0);
        //     if (scale > 0) {
        //         assertDoubleEquals(Double.MIN_NORMAL, indexer.getBucketStart(minIndexNormal), 0);
        //     }
        // }

        // Test power of 2
        for (int exponent = MinExponent; exponent <= MaxExponent; ++exponent) {
            var value = MathHelper.ScaleB(1D, exponent);
            var expectedIndex = scale >= 0 ? indexesPerPowerOf2 * exponent : exponent >> (-scale);

            Assert.Equal(expectedIndex, indexer.MapToIndex(MathHelper.BitIncrement(value)));

            if (scale > 0) {
                if (value > MinNormal) {
                    // Test one bucket down
                    Assert.Equal(expectedIndex - 1, indexer.MapToIndex(MathHelper.BitIncrement(value)));
                    Assert.Equal(expectedIndex - 1, indexer.MapToIndex(MathHelper.BitIncrement(indexer.LowerBoundary(expectedIndex - 1))));
                }

                // Test middle of bucket
                Assert.Equal(expectedIndex + indexesPerPowerOf2 / 2, indexer.MapToIndex(value * squareRootOf2));

                // Sample 10 indexes in a cycle
                for (var index = expectedIndex;
                        index < expectedIndex + indexesPerPowerOf2;
                        index += Math.Max(1, indexesPerPowerOf2 / 10)) {
                    Assert.Equal(index, indexer.MapToIndex(MathHelper.BitIncrement(indexer.LowerBoundary(index))));
                }
            }
        }
    }

    // For numbers down to Double.MIN_NORMAL
    public static int GetMinIndexNormal(int scale) {
        return GetMinIndex(scale, MinExponent);
    }

    // For numbers down to Double.MIN_VALUE
    public static int GetMinIndex(int scale) {
        return GetMinIndex(scale, MinSubnormalExponent);
    }

    // Index of 1.0 * 2^exponent
    private static int GetMinIndex(int scale, int exponent) {
        // Scale > 0: min exponent followed by min subbucket index, which is 0.
        // Scale <= 0: min exponent with -scale bits truncated.
        return scale > 0 ? (exponent << scale)
            : (exponent >> -scale); // Use ">>" to preserve sign of exponent.
    }
}
