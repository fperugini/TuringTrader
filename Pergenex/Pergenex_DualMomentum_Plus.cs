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
    public abstract class Pergenex_DualMomentum_Plus : AlgorithmPlusGlue
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
        protected abstract bool RISKON(int numpairs, double roc1, double roc2, double roc3, double roc4);
        protected virtual int NUM_PICKS { get => 4; }
        protected virtual OrderType ORDER_TYPE => OrderType.closeThisBar;
        protected virtual Universe UNIVERSE { get; set; } = Universes.STOCKS_US_LG_CAP;
        protected virtual string BENCHMARK => Assets.PORTF_60_40;
        protected virtual string ABS_MOMENTUM => "VBMFX";
        protected virtual string RISK_FREE => "BIL";
        #endregion

        #region override public void Run()
        public override IEnumerable<Bar> Run(DateTime? startTime, DateTime? endTime)
        {
            //========== initialization ==========

            WarmupStartTime = DateTime.Parse("09/01/2013", CultureInfo.InvariantCulture);
            StartTime = DateTime.Parse("01/01/2014", CultureInfo.InvariantCulture);
            EndTime = DateTime.Now.Date - TimeSpan.FromDays(-1);

            Deposit(Globals.INITIAL_CAPITAL);
            CommissionPerShare = Globals.COMMISSION; // the book does not deduct commissions

            int num_assets = 0;

            var menu = AddDataSources(ETF_MENU).ToList();

            //	• Invesco DB Base Metals Fund (DBB)
            //	• Invesco DB US Dollar Index Fund (UUP)
            //	• Consumer Discretionary Select Sector SPDR Fund (XLY)
            //	• Consumer Staples Select Sector SPDR Fund (XLP)
            var abm1a = AddDataSource("DBB");
            var abm1b = AddDataSource("UUP");
            var abm2a = AddDataSource("XLY");
            var abm2b = AddDataSource("XLP");
            var rf1 = AddDataSource("TLT");
            var rf2 = AddDataSource("IEF");

            var benchmark = AddDataSource(BENCHMARK);
            //var absMom = AddDataSource(ABS_MOMENTUM);
            //var riskfree = AddDataSource(RISK_FREE);

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
                //if (!HasInstruments(menu) || !HasInstrument(benchmark) || !HasInstrument(absMom))
                //    continue;
                //if (!HasInstruments(menu) || !HasInstrument(abm1a) || !HasInstrument(abm1b) || !HasInstrument(abm2a) || !HasInstrument(abm2b))
                //    continue;

                // determine current S&P 500 constituents
                //var constituents = Instruments
                //    .Where(i => i.IsConstituent(UNIVERSE))
                //    .ToList();

                if (SimTime[0].Month != NextSimTime.Month)
                {
                    // rank, and select top-4 instruments
                    var top4 = menu
                    .Select(ds => ds.Instrument)
                    .OrderByDescending(i => evaluation[i])
                    .Take(NUM_PICKS);

                    // Perform absolute momentum test
                    //var tbil = evaluation.Where(i => i.Key.Nickname == "BIL").First();
                    //var abs = evaluation.Where(i => i.Key.Nickname == "VBMFX").First();
                    //bool riskon = (abs.Value - tbil.Value < 0) ? true : false;
                    var am1 = evaluation.Where(i => i.Key.Nickname == "DBB").First();
                    var am2 = evaluation.Where(i => i.Key.Nickname == "UUP").First();
                    var am3 = evaluation.Where(i => i.Key.Nickname == "XLY").First();
                    var am4 = evaluation.Where(i => i.Key.Nickname == "XLP").First();

                    bool riskon = RISKON(2, am1.Value, am2.Value, am3.Value, am4.Value);

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

    #region Livy
    public class Pergenex_DualMomentum_Dommy : Pergenex_DualMomentum_Plus
    {
        public override string Name => "Pergenex Dual Momentum Dommy";

        //protected override HashSet<string> ETF_MENU => new HashSet<string>()
        //{
        //    //--- equities
        //    "SPY",                 // 1. SPDR S&P 500 ETF Trust
        //    "QQQ",                 // 2. Invesco QQQ Trust
        //    "MDY",                 // 3. SPDR S&P Mid-Cap 400 ETF Trust
        //    "EFA",                 // 4. iShares MSCI EAFE ETF
        //    "FXI",                 // 5. iShares China Large-Cap ETF
        //    "VWO",                 // 6. Vanguard Emerging Markets Stock Index Fund ETF    
        //    "VNQ",                 // 7. Vanguard Real Estate Index ETF
        //    "GLD",                 // 8. SPDR Gold Shares
        //    "TLT",                 // 9. iShares 20+Year Treasury Bond
        //    "LQD",                 // 10. iShares iBoxx $ Inv Grade Corporate Bond ETF
        //    "ILF",                 // 11. iShares Latin America 40 ETF
        //    "PSI",                 // 12. Invesco Dynamic Semiconductors ETF
        //};

        protected override HashSet<string> ETF_MENU => new HashSet<string>()
        {
            "ATVI",	//Activision Blizzard Inc
            "ADBE",	//Adobe Systems Incorporated
            "AKAM",	//Akamai Technologies Inc.
            "ALXN",	//Alexion Pharmaceuticals Inc.
            "GOOG",	//Alphabet Inc.
            "GOOGL",//Alphabet Inc.
            "AMZN",	//Amazon.com Inc.
            "AAL",	//American Airlines Group Inc.
            "AMGN",	//Amgen Inc.
            "ADI",	//Analog Devices Inc.
            "AAPL",	//Apple Inc.
            "AMAT",	//Applied Materials Inc.
            "ADSK",	//Autodesk Inc.
            "ADP",	//Automatic Data Processing Inc.
            "BIDU",	//Baidu Inc.
            "BBBY",	//Bed Bath &amp; Beyond Inc.
            "BIIB",	//Biogen Inc.
            "BMRN",	//BioMarin Pharmaceutical Inc.
            "AVGO",	//Broadcom Limited
            "CA",	//CA Inc.
            "CELG",	//Celgene Corporation
            "CERN",	//Cerner Corporation
            "CHTR",	//Charter Communications Inc.
            "CHKP",	//Check Point Software Technologies Ltd.
            "CSCO",	//Cisco Systems Inc.
            "CTXS",	//Citrix Systems Inc.
            "CTSH",	//Cognizant Technology Solutions Corporation
            "CMCSA",//Comcast Corporation
            "COST",	//Costco Wholesale Corporation
            "CSX",	//CSX Corporation
            //"CTRP",	//Ctrip.com International Ltd.
            "DISCA",//Discovery Communications Inc.
            "DISCK",//Discovery Communications Inc.
            "DISH",	//DISH Network Corporation
            "DLTR",	//Dollar Tree Inc.
            "EBAY",	//eBay Inc.
            "EA",	//Electronic Arts Inc.
            "ENDP",	//Endo International plc
            "EXPE",	//Expedia Inc.
            "ESRX",	//Express Scripts Holding Company
            "FB",	//Facebook Inc.
            "FAST",	//Fastenal Company
            "FISV",	//Fiserv Inc.
            "GILD",	//Gilead Sciences Inc.
            "HSIC",	//Henry Schein Inc.
            "ILMN",	//Illumina Inc.
            "INCY",	//Incyte Corporation
            "INTC",	//Intel Corporation
            "INTU",	//Intuit Inc.
            "ISRG",	//Intuitive Surgical Inc.
            "JD",	//JD.com Inc.
            "LRCX",	//Lam Research Corporation
            "LBTYA",//Liberty Global plc
            "LBTYK",//Liberty Global plc
            "LVNTA",//Liberty Interactive Corporation
            "QVCA",	//Liberty Interactive Corporation
            "LMCA",	//Liberty Media Corporation
            //"LMCK",	//Liberty Media Corporation
            "BATRA",//Liberty Media Corporation
            "BATRK",//Liberty Media Corporation
            "LLTC",	//Linear Technology Corporation
            "MAR",	//Marriott International
            "MAT",	//Mattel Inc.
            "MXIM",	//Maxim Integrated Products Inc.
            "MU",	//Micron Technology Inc.
            "MSFT",	//Microsoft Corporation
            "MDLZ",	//Mondelez International Inc.
            "MNST",	//Monster Beverage Corporation
            "MYL",	//Mylan N.V.
            "NTAP",	//NetApp Inc.
            "NTES",	//NetEase Inc.
            "NFLX",	//Netflix Inc.
            "NCLH",	//Norwegian Cruise Line Holdings Ltd.
            "NVDA",	//NVIDIA Corporation
            "NXPI",	//NXP Semiconductors N.V.
            "ORLY",	//O'Reilly Automotive Inc.
            "PCAR",	//PACCAR Inc.
            "PAYX",	//Paychex Inc.
            "PYPL",	//PayPal Holdings Inc.
            "QCOM",	//QUALCOMM Incorporated
            "REGN",	//Regeneron Pharmaceuticals Inc.
            "ROST",	//Ross Stores Inc.
            "SBAC",	//SBA Communications Corporation
            "STX",	//Seagate Technology PLC
            "SIRI",	//Sirius XM Holdings Inc.
            "SWKS",	//Skyworks Solutions Inc.
            "SBUX",	//Starbucks Corporation
            "SRCL",	//Stericycle Inc.
            //"SYMC",	//Symantec Corporation
            "TMUS",	//T-Mobile US Inc.
            "TSLA",	//Tesla Motors Inc.
            "TXN",	//Texas Instruments Incorporated
            "KHC",	//The Kraft Heinz Company
            "PCLN",	//The Priceline Group Inc.
            "TSCO",	//Tractor Supply Company
            "TRIP",	//TripAdvisor Inc.
            "FOX",	//Twenty-First Century Fox Inc.
            "FOXA",	//Twenty-First Century Fox Inc.
            "ULTA",	//Ulta Salon Cosmetics &amp; Fragrance Inc.
            "VRSK",	//Verisk Analytics Inc.
            "VRTX",	//Vertex Pharmaceuticals Incorporated
            "VIAB",	//Viacom Inc.
            "VOD",	//Vodafone Group Plc
            "WBA",	//Walgreens Boots Alliance Inc.
            "WDC",	//Western Digital Corporation
            "WFM",	//Whole Foods Market Inc.
            "XLNX",	//Xilinx Inc.
            "YHOO",	//Yahoo! Inc.
        };

        protected override bool RISKON(int numpairs, double roc1, double roc2, double roc3, double roc4)
        {
            //Two conditions must be satisfied simultaneously for switching to risk-off allocation:
            //(1) the return of DBB is smaller than that of UUP over the relative strength evaluation period, and (2) similarly, the return of XLY is smaller than that of XLP.
            bool retval2 = true;
            bool retval1 = (roc1 < roc2) ? true : false;
            if (numpairs == 2)
            {
                retval2 = (roc3 < roc4) ? true : false;
            }
            return (retval1 & retval2);
        }

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