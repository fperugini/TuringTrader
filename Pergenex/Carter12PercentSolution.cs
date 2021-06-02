//==============================================================================
// Project:     TuringTrader, algorithms from books & publications
// Name:        David Allen Carter 12% Solution
// Description: Momentum Portfolio
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
        /// <summary>
        /// hash set of asset classes
        /// </summary>
        protected HashSet<Returns> RETURNS { get; set; }
        protected abstract HashSet<string> ETF_MENU { get; }
        protected abstract HashSet<string> RISKFREE_MENU { get; }
        protected abstract double MOMENTUM(Instrument i);
        protected virtual int NUM_PICKS { get => 1; }
        protected virtual OrderType ORDER_TYPE => OrderType.closeThisBar;
        protected virtual string BENCHMARK => Assets.PORTF_60_40;
        protected virtual string RISK_FREE => "BIL";
        #endregion

        #region override public void Run()
        public override IEnumerable<Bar> Run(DateTime? startTime, DateTime? endTime)
        {
            //========== initialization ==========

            WarmupStartTime = DateTime.Parse("09/01/2007", CultureInfo.InvariantCulture);
            StartTime = DateTime.Parse("01/01/2008", CultureInfo.InvariantCulture);
            EndTime = DateTime.Now.Date - TimeSpan.FromDays(0);

            Deposit(Globals.INITIAL_CAPITAL);
            CommissionPerShare = Globals.COMMISSION; // the book does not deduct commissions

            var menu = AddDataSources(ETF_MENU).ToList();
            var riskfree = AddDataSources(ETF_MENU).ToList();
            var benchmark = AddDataSource(BENCHMARK);

            RETURNS = new HashSet<Returns>();

            //========== simulation loop ==========

            foreach (DateTime simTime in SimTimes)
            {
#if false
                if (simTime.Month == 4 && simTime.Year == 2021)
                {
                    int x = 1;
                }
                int day = simTime.Day;
#endif
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
                    // rank, and select top instruments
                    var topriskon = menu
                    .Where(i => menu.Contains(i))
                    .Select(ds => ds.Instrument)
                    .OrderByDescending(i => evaluation[i])
                    .Take(NUM_PICKS);

                    // rank, and select top instruments
                    var topriskfree = menu
                    .Where(i => riskfree.Contains(i))
                    .Select(ds => ds.Instrument)
                    .OrderByDescending(i => evaluation[i])
                    .Take(NUM_PICKS);

                    // Perform absolute momentum test
                    //var tbil = evaluation.Where(i => i.Key.Nickname == "BIL").First();
                    //var abs = evaluation.Where(i => i.Key.Nickname == "VBMFX").First();
                    //bool riskon = (abs.Value - tbil.Value < 0) ? true : false;
                    //bool riskon = RISKON(1, abs.Value, tbil.Value, 0.0, 0.0);

                    // calculate target percentage based on risk status (absolute momentum)
                    double targetEquity = 0.0;
                    double targetPercentage1 = 0.6;
                    double targetPercentage2 = 0.4;
                    double equityPerInstrument1 = NetAssetValue[0] * targetPercentage1;
                    double equityPerInstrument2 = NetAssetValue[0] * targetPercentage2;

                    foreach (var i in menu.Select(ds => ds.Instrument))
                    {
                        // determine current and target shares per instrument...
                        Alloc.Allocation[i] = topriskon.Contains(i) ? targetPercentage1 : 0.0;
                        targetEquity = topriskon.Contains(i) ? equityPerInstrument1 : 0.0;

                        int targetShares = (int)Math.Floor(targetEquity / i.Close[0]);
                        int currentShares = i.Position;
                        Order newOrder = i.Trade(targetShares - currentShares, ORDER_TYPE);

                        // add a comment, to make the trading log easier to read
                        if (newOrder != null)
                        {
                            if (currentShares == 0)
                                newOrder.Comment = "Open";
                            else if (targetShares == 0)
                                newOrder.Comment = "Close";
                            else
                                newOrder.Comment = "Rebalance";
                        }
                    }

                    // plotter output
                    if (!IsOptimizing && TradingDays > 0)
                    {
                        _plotter.AddNavAndBenchmark(this, FindInstrument(BENCHMARK));
                        _plotter.AddStrategyHoldings(this, ETF_MENU.Select(nick => FindInstrument(nick)));
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
    public class Original12PercentSolution : Carter12PercentSolution
    {
        public override string Name => "David Allen Carter 12% Solution";
        protected override HashSet<string> ETF_MENU => new HashSet<string>()
        {
            //--- equities
            "SPY",                 // 1. SPDR S&P 500 ETF Trust
            "QQQ",                 // 2. Invesco QQQ Trust
            "MDY",                 // 3. SPDR S&P Mid-Cap 400 ETF Trust
            "IWM",                 // 4. iShares Russel 2000 ETF
        };
        protected override HashSet<string> RISKFREE_MENU => new HashSet<string>()
        {
            "TLT",                 // 1. SPDR S&P 500 ETF Trust
            "JNK",                 // 2. Invesco QQQ Trust
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