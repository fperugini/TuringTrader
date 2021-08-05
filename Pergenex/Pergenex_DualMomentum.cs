//==============================================================================
// Project:     TuringTrader, algorithms from books & publications
// Name:        Pergenex_DualMomentum
// Description: Dual Momentum Portfolio
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
    public abstract class Pergenex_DualMomentum : AlgorithmPlusGlue
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
        protected abstract double MOMENTUM(Instrument i);
        //protected abstract bool RISKON(int numpairs, double roc1, double roc2, double roc3, double roc4);
        protected virtual int NUM_PICKS { get => 4; }
        protected virtual OrderType ORDER_TYPE => OrderType.closeThisBar;
        protected virtual string BENCHMARK => Assets.PORTF_60_40;
        protected virtual string ABS_MOMENTUM => "VBMFX";
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
            var benchmark = AddDataSource(BENCHMARK);
            var absMom = AddDataSource(ABS_MOMENTUM);
            var riskfree = AddDataSource(RISK_FREE);

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
                if (!HasInstruments(menu) || !HasInstrument(benchmark) || !HasInstrument(absMom))
                    continue;

                if (SimTime[0].Month != NextSimTime.Month)
                {
                    // rank, and select top-4 instruments
                    var top4 = menu
                    .Select(ds => ds.Instrument)
                    .OrderByDescending(i => evaluation[i])
                    .Take(NUM_PICKS);

                    // Perform absolute momentum test
                    var tbil = evaluation.Where(i => i.Key.Nickname == "BIL").First();
                    var abs = evaluation.Where(i => i.Key.Nickname == "VBMFX").First();
                    bool riskon = (abs.Value - tbil.Value < 0) ? true : false;
                    //bool riskon = RISKON(1, abs.Value, tbil.Value, 0.0, 0.0);

                    // calculate target percentage based on risk status (absolute momentum)
                    double targetEquity = 0.0;
                    double targetPercentage = (riskon) ? 1.0 : 1.0 / NUM_PICKS;
                    double equityPerInstrument = (riskon) ? NetAssetValue[0] : NetAssetValue[0] / NUM_PICKS;

                    foreach (var i in menu.Select(ds => ds.Instrument))
                    {
                        // determine current and target shares per instrument...
                        if (riskon)
                        {
                            Alloc.Allocation[i] = (i.Nickname == "TLT") ? targetPercentage : 0.0;
                            targetEquity = (i.Nickname == "TLT") ? equityPerInstrument : 0.0;
                        }
                        else
                        {
                            Alloc.Allocation[i] = top4.Contains(i) ? targetPercentage : 0.0;
                            targetEquity = top4.Contains(i) ? equityPerInstrument : 0.0;
                        }

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

                addMonthlyPerformance();

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
        #region Monthly Performance
        private void addMonthlyPerformance()
        {
            double lastequity = Globals.INITIAL_CAPITAL;
            double startequity = 0;
            double cagr = 0;

            _plotter.SelectChart("Monthly Returns", "Year");
            var series = _plotter.AllData[this.Name];
            for (int i = 0; i < series.Count; i++)
            {
                if (i == series.Count) continue;
                DateTime dt = (DateTime)series[i]["Date"];
                _plotter.SetX(dt.Year.ToString());
                double ret = (double)series[i++][this.Name];
                double pnl = 100.0 * (ret / lastequity - 1.0);
                _plotter.Plot("Jan", pnl.ToString("0.00"));
                lastequity = ret;
                startequity = lastequity;

                if (i == series.Count) continue;
                ret = (double)series[i++][this.Name];
                pnl = 100.0 * (ret / lastequity - 1.0);
                _plotter.Plot("Feb", pnl.ToString("0.00"));
                lastequity = ret;

                if (i == series.Count) continue;
                ret = (double)series[i++][this.Name];
                pnl = 100.0 * (ret / lastequity - 1.0);
                _plotter.Plot("Mar", pnl.ToString("0.00"));
                lastequity = ret;

                if (i == series.Count) continue;
                ret = (double)series[i++][this.Name];
                pnl = 100.0 * (ret / lastequity - 1.0);
                _plotter.Plot("Apr", pnl.ToString("0.00"));
                lastequity = ret;

                if (i == series.Count) continue;
                ret = (double)series[i++][this.Name];
                pnl = 100.0 * (ret / lastequity - 1.0);
                _plotter.Plot("May", pnl.ToString("0.00"));
                lastequity = ret;

                if (i == series.Count) continue;
                ret = (double)series[i++][this.Name];
                pnl = 100.0 * (ret / lastequity - 1.0);
                _plotter.Plot("Jun", pnl.ToString("0.00"));
                lastequity = ret;

                if (i == series.Count) continue;
                ret = (double)series[i++][this.Name];
                pnl = 100.0 * (ret / lastequity - 1.0);
                _plotter.Plot("Jul", pnl.ToString("0.00"));
                lastequity = ret;

                if (i == series.Count) continue;
                ret = (double)series[i++][this.Name];
                pnl = 100.0 * (ret / lastequity - 1.0);
                _plotter.Plot("Aug", pnl.ToString("0.00"));
                lastequity = ret;

                if (i == series.Count) continue;
                ret = (double)series[i++][this.Name];
                pnl = 100.0 * (ret / lastequity - 1.0);
                _plotter.Plot("Sep", pnl.ToString("0.00"));
                lastequity = ret;

                if (i == series.Count) continue;
                ret = (double)series[i++][this.Name];
                pnl = 100.0 * (ret / lastequity - 1.0);
                _plotter.Plot("Oct", pnl.ToString("0.00"));
                lastequity = ret;

                if (i == series.Count) continue;
                ret = (double)series[i++][this.Name];
                pnl = 100.0 * (ret / lastequity - 1.0);
                _plotter.Plot("Nov", pnl.ToString("0.00"));
                lastequity = ret;

                if (i == series.Count) continue;
                ret = (double)series[i][this.Name];
                pnl = 100.0 * (ret / lastequity - 1.0);
                _plotter.Plot("Dec", pnl.ToString("0.00"));
                lastequity = ret;

                cagr = 100 * (Math.Pow((lastequity / startequity), 1) - 1);
                _plotter.Plot("Total", cagr.ToString("0.00"));
            }
        }
        #endregion
    }

    #region Livy
    public class Pergenex_DualMomentum_Livy : Pergenex_DualMomentum
    {
        public override string Name => "Pergenex Dual Momentum Livy";
        protected override HashSet<string> ETF_MENU => new HashSet<string>()
        {
            //--- equities
            "SPY",                 // 1. SPDR S&P 500 ETF Trust
            "QQQ",                 // 2. Invesco QQQ Trust
            "MDY",                 // 3. SPDR S&P Mid-Cap 400 ETF Trust
            "EFA",                 // 4. iShares MSCI EAFE ETF
            "FXI",                 // 5. iShares China Large-Cap ETF
            "VWO",                 // 6. Vanguard Emerging Markets Stock Index Fund ETF    
            "VNQ",                 // 7. Vanguard Real Estate Index ETF
            "GLD",                 // 8. SPDR Gold Shares
            "TLT",                 // 9. iShares 20+Year Treasury Bond
            "LQD",                 // 10. iShares iBoxx $ Inv Grade Corporate Bond ETF
            "ILF",                 // 11. iShares Latin America 40 ETF
            "PSI",                 // 12. Invesco Dynamic Semiconductors ETF
        };

        //protected override bool RISKON(int numpairs, double roc1, double roc2, double roc3, double roc4)
        //{
        //    bool retval2 = true;
        //    bool retval1 = (roc1 - roc2 < 0) ? true : false;
        //    if (numpairs == 2) {
        //        retval2 = (roc3 - roc4 < 0) ? true : false;
        //    }
        //    return (retval1 & retval2);
        //}
        protected override double MOMENTUM(Instrument i)
        {
            // 3 month momentum
            return i.Close[0] / i.Close[63] - 1.0;
        }
    }
    #endregion
}

//As before, we generate signals for market state by using the following ETFs:
//	• Invesco DB Base Metals Fund (DBB)
//	• Invesco DB US Dollar Index Fund (UUP)
//	• Consumer Discretionary Select Sector SPDR Fund (XLY)
//	• Consumer Staples Select Sector SPDR Fund (XLP)
//Two conditions must be satisfied simultaneously for switching to risk-off allocation: (1) the return of DBB is smaller than that of UUP over the relative strength evaluation period, and (2) similarly, the return of XLY is smaller than that of XLP .

//==============================================================================
// end of file