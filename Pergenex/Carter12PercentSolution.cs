//==============================================================================
// Project:     TuringTrader, algorithms from books & publications
// Name:        David Allen Carter 12% Solution
// Description: Momentum Portfolio
// 60 % of Portfolio:
// Once a month, at the close of the last trading day of the month, the strategy determines the top-performing equity ETF listed in Assets,
// using a simple momentum test.With all four equity funds listed on a comparison chart (along with SHY*), and using a 3 - month lookback,
// the ETF whose graph line is uppermost on the chart is the outperformer. The strategy buys that fund at the close on the first trading day of the month.
// Cash Trigger: Should none of the four graph lines be trending above the 0.0% line (or more precisely, the graph line for SHY *) on that last day of the month,
// this portion of the portfolio goes to cash.
//
// 40% of Portfolio:
// At the same time, the strategy determines the top-performing bond ETF listed in Assets, using the same momentum test.
// It buys that fund.No cash trigger on the bond side.
//
// So, at any given time The 12% Solution holds one equity index ETF (or cash) representing 60% of the portfolio, and one bond ETF comprising the remaining 40% of the portfolio.
//
// History:     2021, FP, created
//------------------------------------------------------------------------------
// Copyright:   (c) 2021, Pergenex Software LLC
//              https://www.pergenex.com
//==============================================================================

#region libraries
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TuringTrader.Algorithms.Glue;
using TuringTrader.Simulator;
#endregion

namespace TuringTrader.Pergenex
{
    public abstract class Carter12PercentSolution : AlgorithmPlusGlue
    {
        #region inputs
        protected class Returns
        {
            public string name;
            public string nickname;
            public double roc;
        }
        protected HashSet<Returns> RETURNS { get; set; }
        protected double RISKON_WEIGHT = 0.6;
        protected double HEDGE_WEIGHT = 0.4;
        protected abstract HashSet<string> RISK_ON_ASSETS { get; }
        protected abstract HashSet<string> HEDGE_ASSETS { get; }
        protected abstract double MOMENTUM(Instrument i);
        protected virtual int NUM_PICKS { get => 1; }
        protected virtual string BENCHMARK => Assets.PORTF_60_40;
        #endregion

        #region override public void Run()
        public override IEnumerable<Bar> Run(DateTime? startTime, DateTime? endTime)
        {
            //========== initialization ==========

            WarmupStartTime = DateTime.Parse("09/01/2007", CultureInfo.InvariantCulture);
            StartTime = DateTime.Parse("01/01/2008", CultureInfo.InvariantCulture);
            EndTime = DateTime.Now.Date - TimeSpan.FromDays(0);

            Deposit(Globals.INITIAL_CAPITAL);
            CommissionPerShare = Globals.COMMISSION;

            var menu = AddDataSources(RISK_ON_ASSETS).ToList();
            var riskfree = AddDataSources(HEDGE_ASSETS).ToList();
            var benchmark = AddDataSource(BENCHMARK);

            RETURNS = new HashSet<Returns>();

            //========== simulation loop ==========

            foreach (DateTime simTime in SimTimes)
            {
                // calculate momentum w/ algorithm-specific helper function
                var evaluation = Instruments
                    .ToDictionary(
                        i => i,
                        i => MOMENTUM(i));

                RETURNS.Clear();
                foreach (var i in evaluation)
                    RETURNS.Add(new Returns { name = i.Key.Name, nickname = i.Key.Nickname, roc = i.Value });

                RETURNS = RETURNS.OrderByDescending(i => i.roc).ToHashSet();

                // skip, if there are any missing instruments
                if (!HasInstruments(menu) || !HasInstrument(benchmark) || !HasInstruments(riskfree))
                    continue;

                if (SimTime[0].Month != NextSimTime.Month)
                {
                    // rank, and select top riskon instrument
                    var topriskon = menu
                    .Select(ds => ds.Instrument)
                    .OrderByDescending(i => evaluation[i])
                    .Take(NUM_PICKS);

                    // rank, and select top hedge instrument
                    var topriskfree = riskfree
                    .Select(ds => ds.Instrument)
                    .OrderByDescending(i => evaluation[i])
                    .Take(NUM_PICKS);

                    // create empty structure for instrument weights
                    Dictionary<Instrument, double> instrumentWeights = menu.Union(riskfree).ToDictionary(ds => ds.Instrument, ds => 0.0);
                    var riskonmomo = evaluation.Where(i => i.Key.Nickname == topriskon.ElementAt(0).Nickname).First();           
                    instrumentWeights[topriskon.ElementAt(0)] = (riskonmomo.Value > 0) ? RISKON_WEIGHT : 0.0;
                    instrumentWeights[topriskfree.ElementAt(0)] = HEDGE_WEIGHT;

                    // create orders
                    foreach (var i in instrumentWeights.Keys)
                    {
                        Alloc.Allocation[i] = instrumentWeights[i];
                        int targetShares = (int)Math.Floor(instrumentWeights[i] * NetAssetValue[0] / i.Close[0]);
                        int currentShares = i.Position;
                        Order newOrder = i.Trade(targetShares - currentShares);

                        if (newOrder != null)
                        {
                            if (currentShares == 0) newOrder.Comment = "open";
                            else if (targetShares == 0) newOrder.Comment = "close";
                            else newOrder.Comment = "rebalance";
                        }
                    }

                    // plotter output
                    if (!IsOptimizing && TradingDays > 0)
                    {
                        _plotter.AddNavAndBenchmark(this, FindInstrument(BENCHMARK));
                        _plotter.AddStrategyHoldings(this, RISK_ON_ASSETS.Union(HEDGE_ASSETS).Select(nick => FindInstrument(nick)));
                        if (Alloc.LastUpdate == SimTime[0])
                            _plotter.AddTargetAllocationRow(Alloc);

                        if (IsDataSource)
                        {
                            var v = 10.0 * NetAssetValue[0] / Globals.INITIAL_CAPITAL;
                            yield return Bar.NewOHLC(this.GetType().Name, SimTime[0], v, v, v, v, 0);
                        }
                    }
                }
            }
            //========== post processing ==========

            if (!IsOptimizing)
            {
                _plotter.AddTargetAllocation(Alloc);
                _plotter.AddOrderLog(this);
                _plotter.AddPositionLog(this);
                _plotter.AddPnLHoldTime(this);
                _plotter.AddMfeMae(this);
                _plotter.AddParameters(this);

                _plotter.SelectChart("Current Returns", "Name");
                foreach (var i in RETURNS)
                {
                    _plotter.SetX(i.nickname);
                    _plotter.Plot("Symbol", i.name);
                    _plotter.Plot("3-month return", String.Format("Value: {0:P2}.", i.roc));
                }
            }

            FitnessValue = this.CalcFitness();
        }
        #endregion
    }

    #region Original 12% Solution from book
    public class CarterOriginal12PercentSolution : Carter12PercentSolution
    {
        public override string Name => "David Allen Carter 12% Solution";
        protected override HashSet<string> RISK_ON_ASSETS => new HashSet<string>()
        {
            //--- equities
            "SPY",                 // 1. SPDR S&P 500 ETF Trust
            "QQQ",                 // 2. Invesco QQQ Trust
            "MDY",                 // 3. SPDR S&P Mid-Cap 400 ETF Trust
            "IWM",                 // 4. iShares Russel 2000 ETF
        };
        protected override HashSet<string> HEDGE_ASSETS => new HashSet<string>()
        {
            "TLT",                 // 1. iShares 20+Year Treasury Bond
            "JNK",                 // 2. iShares 20+ Year Treasury Bond ETF
        };

        protected override double MOMENTUM(Instrument i)
        {
            // 3 month momentum
            return i.Close[0] / i.Close[63] - 1.0;
        }
    }
    #endregion
}

//==============================================================================
// end of file