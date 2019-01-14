﻿//==============================================================================
// Project:     Trading Simulator
// Name:        OptionSupport
// Description: unit test for option support class.
// History:     2019i14, FUB, created
//------------------------------------------------------------------------------
// Copyright:   (c) 2017-2019, Bertram Solutions LLC
//              http://www.bertram.solutions
// License:     this code is licensed under GPL-3.0-or-later
//==============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TuringTrader.Simulator;

namespace SimulatorEngine.Tests
{
    [TestClass]
    public class OptionSupport
    {
        #region class TestSimulator
        private class TestSimulator : SimulatorCore
        {
            private List<DataSource> _testDataSources;
            private double _iv;
            private double _delta;
            private double _gamma;
            private double _theta;
            private double _vega;
            private double _riskFree;
            private double _divYield;

            public TestSimulator(List<DataSource> testDataSources, double riskFree, double divYield, double iv, double delta, double gamma, double theta, double vega)
            {
                _testDataSources = testDataSources;

                _riskFree = riskFree;
                _divYield = divYield;

                _iv = iv;
                _delta = delta;
                _gamma = gamma;
                _theta = theta;
                _vega = vega;

                Run();
            }

            public override void Run()
            {
                StartTime = DateTime.Parse("01/01/2000");
                EndTime = DateTime.Parse("12/31/2019");

                foreach (var dataSource in _testDataSources)
                    AddDataSource(dataSource);

                foreach (DateTime simTime in SimTimes)
                {
                    Instrument underlying = Instruments
                        .Where(i => i.IsOption == false)
                        .First();

                    Instrument option = Instruments
                        .Where(i => i.IsOption == true)
                        .First();

                    double riskFreeRate = _riskFree;
                    double dividendYield = _divYield;

                    var iv = option.BlackScholes(riskFreeRate, dividendYield);
                    var greeks = option.BlackScholes(iv.Volatility, riskFreeRate, dividendYield);

                    //Output.WriteLine("iv={0}, delta={1}, gamma={2}, theta={3}, vega={4}",
                    //    iv.Volatility, greeks.Delta, greeks.Gamma, greeks.Theta, greeks.Vega);

                    //Assert.IsTrue(Math.Abs(iv.Price - greeks.Price) < 1e-5);
                    Assert.IsTrue(Math.Abs(iv.Volatility - _iv) < 1e-5);
                    Assert.IsTrue(Math.Abs(greeks.Delta - _delta) < 1e-5);
                    Assert.IsTrue(Math.Abs(greeks.Gamma - _gamma) < 1e-5);
                    Assert.IsTrue(Math.Abs(greeks.Theta - _theta) < 1e-5);
                    Assert.IsTrue(Math.Abs(greeks.Vega - _vega) < 1e-5);
                }
            }
        }
        #endregion
        #region class TestVector
        private class TestVector
        {
            //--- stimuli
            // underlying
            public DateTime quoteDate;
            public double underlyingLast;
            // market
            public double riskFreeRate;
            public double dividendYield;
            // option
            public DateTime expiration;
            public double strike;
            public bool isPut;
            public double bid;
            public double ask;

            //--- responses
            public double impliedVol;
            public double delta;
            public double gamma;
            public double theta;
            public double vega;

        }
        #endregion

        #region public void Test_PriceAndGreeks()
        [TestMethod]
        public void Test_PriceAndGreeksNew()
        {
            List<TestVector> testVectors = new List<TestVector>
            {
                new TestVector
                {
                    quoteDate = DateTime.Parse("10/01/2015"),
                    underlyingLast = 1921.42,
                    riskFreeRate = 0.024, dividendYield = 0.018,
                    expiration = DateTime.Parse("10/16/2015"),
                    strike = 1845, isPut = false, bid = 85.80, ask = 90.00,
                    // impliedVol = 0.2538, delta = 0.798, gamma = 0.0029, theta = -351.149, vega = 107.226 // from historical data
                    impliedVol = 0.247698736469922, delta = 0.798543648749816, gamma = 0.00290809275058303, theta = -336.456110965229, vega = 109.213844026198 // set to match
                },
                new TestVector
                {
                    quoteDate = DateTime.Parse("10/01/2015"),
                    underlyingLast = 1921.42,
                    riskFreeRate = 0.024, dividendYield = 0.018,
                    expiration = DateTime.Parse("10/16/2015"),
                    strike = 1980, isPut = false, bid = 6.80, ask = 8.20,
                    // impliedVol = 0.177, delta = 0.2018, gamma = 0.0042, theta = -242.777, vega = 107.183 // from historical data
                    impliedVol = 0.172841571785126, delta = 0.202310532937698, gamma = 0.00418736136877768, theta = -233.066963429968, vega = 109.732253332688 // set to match
                },
                new TestVector
                {
                    quoteDate = DateTime.Parse("10/01/2015"),
                    underlyingLast = 1921.42,
                    riskFreeRate = 0.024, dividendYield = 0.018,
                    expiration = DateTime.Parse("10/16/2015"),
                    strike = 1845, isPut = true, bid = 9.40, ask = 11.60,
                    // impliedVol = 0.2472, delta = -0.1961, gamma = 0.0029, theta = -330.323, vega = 105.344 // from historical data
                    impliedVol = 0.242329894301333, delta = -0.195851223514913, gamma = 0.00292882797276422, theta = -314.974565405346, vega = 107.608482003309 // set to match
                },
                new TestVector
                {
                    quoteDate = DateTime.Parse("10/01/2015"),
                    underlyingLast = 1921.42,
                    riskFreeRate = 0.024, dividendYield = 0.018,
                    expiration = DateTime.Parse("10/16/2015"),
                    strike = 1980, isPut = true, bid = 63.20, ask = 67.90,
                    // impliedVol = 0.1734, delta = -0.8032, gamma = 0.0042, theta = -227.891, vega = 105.562 // from historical data
                    impliedVol = 0.172848420338619, delta = -0.796940829541531, gamma = 0.00418731537928118, theta = -220.169637364834, vega = 109.735396057305 // set to match
                },
            };

            foreach (var testVector in testVectors)
            {
                //--- create data source for underlying
                Dictionary<DataSourceValue, string> underlyingInfos = new Dictionary<DataSourceValue, string>
                {
                    { DataSourceValue.name, "S&P 500 Index" }
                };

                List<Bar> underlyingBars = new List<Bar>
                {   new Bar(
                        "SPX", testVector.quoteDate,
                        testVector.underlyingLast, testVector.underlyingLast, testVector.underlyingLast, testVector.underlyingLast, 100, true,
                        default(double), default(double), default(long), default(long), false,
                        default(DateTime), default(double), default(bool))
                };
                DataSource underlyingDataSource = new DataSourceFromBars(underlyingBars, underlyingInfos);

                //--- create data source for option
                Dictionary<DataSourceValue, string> optionInfos = new Dictionary<DataSourceValue, string>
                {
                    { DataSourceValue.name, "S&P 500 Index Options" },
                    { DataSourceValue.optionUnderlying, "SPX" }
                };
                List<Bar> optionBars = new List<Bar>
                {   new Bar(
                        "SPX_Option", testVector.quoteDate,
                        default(double), default(double), default(double), default(double), default(long), false,
                        testVector.bid, testVector.ask, 100, 100, true,
                        testVector.expiration, testVector.strike, testVector.isPut)
                };
                DataSource optionDataSource = new DataSourceFromBars(optionBars, optionInfos);

                //--- run test
                SimulatorCore callSim = new TestSimulator(
                    new List<DataSource> { underlyingDataSource, optionDataSource },
                    testVector.riskFreeRate, testVector.dividendYield,
                    testVector.impliedVol, testVector.delta, testVector.gamma, testVector.theta, testVector.vega
                );
            }
        }
        #endregion
    }
}

//==============================================================================
// end of file