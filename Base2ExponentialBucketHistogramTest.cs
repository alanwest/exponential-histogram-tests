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

#if !NETFRAMEWORK
using System;
using System.Collections.Generic;
using OpenTelemetry.Tests;
using Xunit;

namespace OpenTelemetry.Metrics.Tests;

public class Base2ExponentialBucketHistogramTest
{
    // ~2.2250738585072014E-308 (minimum normal positive, 2 ^ -1022)
    private static double MinNormal = IEEE754Double.FromString("0 00000000001 0000000000000000000000000000000000000000000000000000").DoubleValue;

    private const int MinExponent = -1022;
    private const int MaxExponent = 1023;
    private const int MinSubnormalExponent = MinExponent - 52;

    public static IEnumerable<object[]> Scales =>
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
    [MemberData(nameof(Scales))]
    public void TestScales(int scale)
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
        Assert.Equal(0, indexer.MapToIndex(Math.BitIncrement(1D)));

        // Test min and max.
        var maxIndex = GetMaxIndex(scale);
        var minIndexNormal = GetMinIndexNormal(scale);
        var minIndex = GetMinIndex(scale);

        // Max, min normal, min value to index
        Assert.Equal(maxIndex, indexer.MapToIndex(double.MaxValue));
        Assert.Equal(minIndexNormal, indexer.MapToIndex(MinNormal));
        Assert.Equal(minIndex, indexer.MapToIndex(double.Epsilon));

        // Max, min normal round trip
        Assert.Equal(maxIndex, indexer.MapToIndex(Math.BitIncrement(indexer.LowerBoundary(maxIndex))));
        Assert.Equal(minIndexNormal, indexer.MapToIndex(Math.BitIncrement(indexer.LowerBoundary(minIndexNormal))));

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
            var value = Math.ScaleB(1D, exponent);
            var expectedIndex = scale >= 0 ? indexesPerPowerOf2 * exponent : exponent >> (-scale);

            Assert.Equal(expectedIndex, indexer.MapToIndex(Math.BitIncrement(value)));

            if (scale > 0) {
                if (value > MinNormal) {
                    // Test one bucket down
                    Assert.Equal(expectedIndex - 1, indexer.MapToIndex(Math.BitIncrement(value)));
                    Assert.Equal(expectedIndex - 1, indexer.MapToIndex(Math.BitIncrement(indexer.LowerBoundary(expectedIndex - 1))));
                }

                // Test middle of bucket
                Assert.Equal(expectedIndex + indexesPerPowerOf2 / 2, indexer.MapToIndex(value * squareRootOf2));

                // Sample 10 indexes in a cycle
                for (var index = expectedIndex;
                        index < expectedIndex + indexesPerPowerOf2;
                        index += Math.Max(1, indexesPerPowerOf2 / 10)) {
                    Assert.Equal(index, indexer.MapToIndex(Math.BitIncrement(indexer.LowerBoundary(index))));
                }
            }
        }
    }

    // For numbers up to Double.MAX_VALUE
    private static int GetMaxIndex(int scale) {
        // Scale > 0: max exponent followed by max subbucket index.
        // Scale <= 0: max exponent with -scale bits truncated.
        return scale > 0 ? ((MaxExponent << scale) | ((1 << scale) - 1))
                : (MaxExponent >>> -scale);
    }

    // For numbers down to Double.MIN_NORMAL
    private static int GetMinIndexNormal(int scale) {
        return GetMinIndex(scale, MinExponent);
    }

    // For numbers down to Double.MIN_VALUE
    private static int GetMinIndex(int scale) {
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
#endif
