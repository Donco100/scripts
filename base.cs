#region Using declarations
using System;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Indicator;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Strategy;
#endregion



// This namespace holds all strategies and is required. Do not change it.
namespace NinjaTrader.Strategy
{
    /// <summary>
    /// Automated range trading
	/*
		devices:					-	methods
					barDetector		-	detect events on close of a 5 min bar
					tickDetector	-	detect events tick by tick
					eventProcess	-	event consumer and state machine
					execution		-   trade state machine
		events:
					DETECT			-   range detected
					BREAKOUT		-   tick through range
					BREAKOUT_CLOSE	-	bar close outside the range
					END				-   range ended
		
		action:		SWING_IN		-	go from one side of the range to the other
					SWING_OUT		-	on breakout follow breakout direction - side effect - ends range
		state:
					range
					trade
	
	*/
	
	
	
    /// </summary>
    [Description("Base functionality implementation")]
    abstract public class Base : Strategy
    {
		#region Types
		public struct Trade{
			public double entry;
			public double target;
			public double stop;
			public bool pending;
			public int enteredBar;
			public string type;
			public string signal;
			public int dir;
			public int enteredContracts;
		
		};
		
		public struct Tick{
			public double bid;
			public double ask;
			public int	iTime;
		};
		public struct Execution{
			public IOrder entryOrder;
			public IOrder exitOrder;
			public IOrder stopOrder;
			public string orderID;
			public int  pendingBar;
			public bool pendingLongEntry;
			public bool pendingShortEntry;
			public bool pendingLongExit;
			public bool pendingShortExit;
		};
		
		#endregion
		
        #region Variables
        //properties
		private int     orderBarIndex		=	1;   																// DataSeries 1 for all orders
		private int 	numContracts 		= 	1; 																	// Default setting for NumContracts
		private bool 	tradingLive			=	false; 																//Default setting for TradingLive
		private string 	contractMonth		=	"12-13"; 															//Default setting for ContractMonth
		private string 	strategyName		=	"Base";  															//Default setting for Name
		private string 	instrumentName		=	"ES";
		private double  minmargin  			=	750.0;
		private double  maxmargin			=	1000;
		
		//how far past the line to go before event is triggered
		
		protected Trade 		trade			=	new Trade();
		protected Execution 	ex				=	new Execution();
		protected int 			bar				=	0;	
		protected Tick			tick			=	new Tick();
		protected int 			currentBar		=	0;
		protected double 		gainTotal		=	1500.0;															//gain accumulator
		protected int 			iStartTime		=	20000;
		protected int 			iLastEntryTime	=	153000;
		protected int 			iExitOnCloseTime=	155800;
		protected int 			iRestartTime	=	181500;
		protected double		tf				=	4;																//tick fraction - how many ticks per point; 4 for QM, 1 for YM
		protected DateTime 		t;																					//current exchange time
		protected int 			lineCount		=	0;																//shift text lines on the chart so that they do not overlap
		
        #endregion
		protected abstract void start();
		protected override void OnStartUp(){
			start();
			initLog();
			log("START "+strategyName);
			
		}

		/// <summary>
		/// This method is used to configure the strategy and is called once before any strategy method is called.
		/// </summary>
		protected override void Initialize(){
			EntryHandling = EntryHandling.UniqueEntries;
			TraceOrders = false; 
			CalculateOnBarClose = true;
			IncludeCommission=true;
			BarsRequired=48;
			string firstInstrumentSpec=instrumentName+" "+contractMonth;
			
			Add(firstInstrumentSpec,PeriodType.Tick,1);																//1
			Add(firstInstrumentSpec,PeriodType.Minute,5,MarketDataType.Bid); 										//2
			Add(firstInstrumentSpec,PeriodType.Minute,5,MarketDataType.Ask); 										//3 included in live for accurate timestamps
			Add(firstInstrumentSpec,PeriodType.Tick,1,MarketDataType.Bid); 											//4 included in live for accurate timestamps
			Add(firstInstrumentSpec,PeriodType.Tick,1,MarketDataType.Ask); 											//5 included in live for accurate timestamps
		}
	
		/// <summary>
		/// Called on each bar update event (incoming tick)
		/// </summary>
		protected override void OnBarUpdate(){
			t=Times[1][0];																							//set time for logging
			tick.bid=Closes[4][0];
			tick.ask=Closes[5][0];
			tick.iTime=ToTime(Time[0]);
		
			if(BarsInProgress==0){
				if(CurrentBar==currentBar)
					return;
				currentBar=CurrentBar;
				tick0(CurrentBar);																					//misc bar 0 processing
				if(!Historical){
					tick.bid=GetCurrentBid(1);
					tick.ask=GetCurrentAsk(1);
				}
				barDetector();
				if(!Historical){
					logState("HEARTBEAT["+strategyName+"]");
				}
			}
			if(BarsInProgress==4||BarsInProgress==5||BarsInProgress==0){
				if(Historical){ // when backtesting to the trades
					if(checkMarginCall())
						tickDetector();
				}
				else if(!Historical){
					
					tick.bid=GetCurrentBid(1);
					tick.ask=GetCurrentAsk(1);
					if(checkMarginCall())
						tickDetector();
					//heartbeat();
				}
			}
		}
		protected void checkNumContracts(){
				if(!Historical){
					gainTotal=GetAccountValue(AccountItem.CashValue)/maxmargin;
				}
				NumContracts=(int)(gainTotal/maxmargin);
				if(NumContracts==0||gainTotal<maxmargin)
					Disable();
				log("CHECK_NUM_CONTRACTS:"+NumContracts);
		}
		protected bool checkMarginCall(){
			Position pES=Positions[OrderBarIndex];
			int q=pES.Quantity;
			if(pES.MarketPosition==MarketPosition.Long){
				double delta= (tick.bid-trade.entry)*tf*12.5*q;
				double value=gainTotal+delta;
				double vpc=value/q;
				if(vpc<minmargin){
					logState("MINMARGIN CALL delta="+delta+";value="+value+";vpc="+vpc);
					trade.signal="MINMARGIN CALL";
					exitLongMarket();
					return false;
				}
			}
			else if(pES.MarketPosition==MarketPosition.Short){
				double delta= (trade.entry-tick.ask)*q;
				double value=gainTotal+delta;
				double vpc=value/q;
				if(vpc<minmargin){
					logState("MINMARGIN CALL delta="+delta+";value="+value+";vpc="+vpc);
					trade.signal="MINMARGIN CALL";
					exitShortMarket();
					return false;
				}
			}
			return true;
		}
		abstract protected  void barDetector();
		
		abstract protected  void tickDetector();
			
		
		protected virtual int getExitTarget(){
			
				return (int)trade.target;
			
		}
		protected virtual int getExitStop(){
			if(getPos()>0){
				return (int) trade.stop;
			}
			else if (getPos()<0){
				return (int) trade.stop;
				
			}
			return 0;
		}
		/*************************************************
		EXECUTION
		**************************************************/
			
		// only called on bar[0]
		protected void tick0(int bar){
			this.bar=bar;
			checkNumContracts();
			if(bar>ex.pendingBar+1){																		//taking care of stuck orders
				if(ex.pendingLongEntry||ex.pendingShortEntry){
					if(ex.entryOrder!=null){
						CancelOrder(ex.entryOrder);
						if(Historical){
							ex.pendingLongEntry=false;
							ex.pendingShortEntry=false;
							trade.pending=false;
						}
						log("<<< CANCELLING ENTRY ORDER >>> pendingBar="+ex.pendingBar
							+";CurrentBar"+CurrentBar+";ask="+tick.ask+";bid="+tick.bid);
					}
				}
			}
		}
		
		protected void enterLong(double limit){
			/*if(Historical&&tradingLive)
				return;*/
			trade.pending=true;
			trade.enteredContracts=0;
			ex.pendingLongEntry=true;
			ex.pendingBar=bar;
			ex.orderID=trade.signal;
			log("SETTING LONG EXIT: TARGET="+getExitTarget()+";STOP="+getExitStop()+";SIGNAL="+trade.signal);			
			SetProfitTarget(trade.signal,CalculationMode.Ticks,getExitTarget());
			SetStopLoss(trade.signal,CalculationMode.Ticks,getExitStop(),false);
			
			ex.entryOrder=EnterLongLimit(OrderBarIndex,true,NumContracts,limit,ex.orderID);
		}
		protected void enterLongMarket(){
			/*if(Historical&&tradingLive)
				return;*/
			trade.pending=true;
			trade.enteredContracts=0;
			ex.pendingLongEntry=true;
			ex.pendingBar=bar;
			ex.orderID=trade.signal;
			log("SETTING LONG EXIT: TARGET="+getExitTarget()+";STOP="+getExitStop()+";SIGNAL="+trade.signal);			
			SetProfitTarget(trade.signal,CalculationMode.Ticks,getExitTarget());
			SetStopLoss(trade.signal,CalculationMode.Ticks,getExitStop(),false);
			
			ex.entryOrder=EnterLong(OrderBarIndex,NumContracts,ex.orderID);
		}
		protected void enterShort( double limit){
			/*if(Historical&&tradingLive)
				return;*/
			trade.pending=true;
			trade.enteredContracts=0;
			ex.pendingShortEntry=true;
			ex.pendingBar=bar;
			ex.orderID=trade.signal;
			log("SETTING SHORT EXIT: TARGET="+getExitTarget()+";STOP="+getExitStop()+";SIGNAL="+trade.signal);			
			SetProfitTarget(trade.signal,CalculationMode.Ticks,getExitTarget());
			SetStopLoss(trade.signal,CalculationMode.Ticks,getExitStop(),false);
			
			ex.entryOrder=EnterShortLimit(OrderBarIndex,true,NumContracts,limit,ex.orderID);
		}
		protected void enterShortMarket(){
			/*if(Historical&&tradingLive)
				return;*/
			trade.pending=true;		
			trade.enteredContracts=0;	
			ex.pendingShortEntry=true;
			ex.pendingBar=bar;
			ex.orderID=trade.signal;
			log("SETTING SHORT EXIT: TARGET="+getExitTarget()+";STOP="+getExitStop()+";SIGNAL="+trade.signal);			
			SetProfitTarget(trade.signal,CalculationMode.Ticks,getExitTarget());
			SetStopLoss(trade.signal,CalculationMode.Ticks,getExitStop(),false);
			
			ex.entryOrder=EnterShort(OrderBarIndex,NumContracts,ex.orderID);
		}
		protected void exitShort(double limit){
			/*if(Historical&&tradingLive)
				return;*/
			trade.pending=true;
			ex.pendingShortExit=true;
			ex.pendingBar=bar;
			ex.exitOrder=ExitShortLimit(OrderBarIndex,true,trade.enteredContracts,limit,trade.signal,ex.orderID);
		}
		protected void exitShortMarket(){
			/*if(Historical&&tradingLive)
				return;*/
			trade.pending=true;
			ex.pendingShortExit=true;
			ex.pendingBar=bar;
			ex.exitOrder=ExitShort(OrderBarIndex,trade.enteredContracts,trade.signal,ex.orderID);
		}
		protected void exitLong(double limit){
			/*if(Historical&&tradingLive)
				return;*/
			trade.pending=true;
			ex.pendingLongExit=true;
			ex.pendingBar=bar;
			ex.exitOrder=ExitLongLimit(OrderBarIndex,true,trade.enteredContracts,limit,trade.signal,ex.orderID);
		}
		protected void exitLongMarket(){
			/*if(Historical&&tradingLive)
				return;*/
			trade.pending=true;
			ex.pendingLongExit=true;
			ex.pendingBar=bar;
			ex.exitOrder=ExitLong(OrderBarIndex,trade.enteredContracts,trade.signal,ex.orderID);
		}
		protected int getPos(){
			Position pES=Positions[OrderBarIndex];
			if(pES.MarketPosition==MarketPosition.Long)
				return 1;
			if(pES.MarketPosition==MarketPosition.Short)
				return -1;
			return 0;
		}
		protected override void OnOrderUpdate(IOrder order){
			if (ex.entryOrder != null && ex.entryOrder == order){
				if (order.OrderState == OrderState.Cancelled){
					log("---->>>> Entry Order Cancelled");
					ex.pendingLongEntry=false;
					ex.pendingShortEntry=false;
					trade.pending=false;
					ex.entryOrder = null;
				 }
			}
			else if (ex.exitOrder != null && ex.exitOrder == order){
				//log(order.ToString());
				if (order.OrderState == OrderState.Cancelled){
					log("---->>>> Exit Order Cancelled");
					ex.pendingLongExit=false;
					ex.pendingShortExit=false;
					trade.pending=false;
					ex.exitOrder = null;
				 }
			}
		}
		protected override void OnPositionUpdate(IPosition position){
			checkNumContracts();
			if(trade.pending){
				if (Positions[OrderBarIndex].MarketPosition == MarketPosition.Flat){
					if(ex.pendingLongExit||ex.pendingShortExit){
						logState("EXITED FLAT signal="+trade.signal);
						trade.pending=false;
						ex.pendingLongExit=false;
						ex.pendingShortExit=false;
						trade.entry=0;
					}
				}
				if(ex.pendingLongEntry){
					if (Positions[OrderBarIndex].MarketPosition == MarketPosition.Long){
						logState("POSITION: ENTERED LONG signal="+trade.signal);
						trade.pending=false;
						ex.pendingLongEntry=false;
						/*if(tradingLive){
							log("SETTING LONG EXIT: TARGET="+getExitTarget()+";STOP="+getExitStop()+";SIGNAL="+trade.signal);			
							SetProfitTarget(trade.signal,CalculationMode.Ticks,getExitTarget());
							SetStopLoss(trade.signal,CalculationMode.Ticks,getExitStop(),false);
						}*/
					}
				}
				else if(ex.pendingShortEntry){
					if (Positions[OrderBarIndex].MarketPosition == MarketPosition.Short){
						logState("POSITION: ENTERED SHORT signal="+trade.signal);
						trade.pending=false;
						ex.pendingShortEntry=false;
							
						/*if(tradingLive){
							log("SETTING SHORT EXIT: TARGET="+getExitTarget()+";STOP="+getExitStop()+";SIGNAL="+trade.signal);			
							SetProfitTarget(trade.signal,CalculationMode.Ticks,getExitTarget());
							SetStopLoss(trade.signal,CalculationMode.Ticks,getExitStop(),false);
						}*/
					}
				}
			}
		}
		protected override void OnExecution(IExecution execution){
			checkNumContracts();
			if (execution.Order != null && execution.Order.OrderState == OrderState.Filled){
				//log("Execution:"+execution.ToString());
				if (ex.entryOrder != null && ex.entryOrder == execution.Order){
					trade.entry=execution.Order.AvgFillPrice;
					trade.enteredBar=bar;
					trade.enteredContracts+=execution.Quantity;
					log("EXECUTION: ENTERED["+execution.Quantity+"] at "+trade.entry+";totalEntered="+trade.enteredContracts);
					
					//trade.pending=false;
					//ex.pendingShortEntry=false;
				}
				else{
					if(execution.Order.OrderAction.CompareTo(OrderAction.Sell)==0){
						ex.pendingLongExit=true;
						ex.pendingShortExit=false;
					}
					else{
						ex.pendingShortExit=true;
						ex.pendingLongExit=false;
					}
					double currentExit=execution.Order.AvgFillPrice;
					double gain=0;
					
					if(ex.pendingLongExit){
						gain=(currentExit-trade.entry);
					}else if(ex.pendingShortExit){
						gain=(trade.entry-currentExit);
					}
					double net_gain=(gain*4*12.5-4)*execution.Quantity;
					gainTotal+=net_gain;
					log("EXITED{"+execution.Name+"} at "+currentExit+";$$$$$$ gain="+gain+";net="+net_gain.ToString("C")+"; $$$$$ total="+gainTotal.ToString("C"));
				
					if(ex.stopOrder!=null&&execution.Order!=ex.stopOrder){
						CancelOrder(ex.stopOrder);
					}
					else if(ex.exitOrder!=null&&execution.Order==ex.exitOrder){	
						CancelOrder(ex.exitOrder);	
					}	
					ex.exitOrder=null;		
					ex.stopOrder=null;
					if(net_gain<0){
						reportLoss();
					}
					else{
						reportWin();
					}
			
				}	
			}
		}
		protected virtual void reportLoss(){}
		protected virtual void reportWin(){}
		protected override void OnTermination(){
  			string s="hello";
			//string s="Finished Simulated Session "+strategyName+"  "+ToDay(firstTime)+"-"+ToDay(t)+"\n 	Range="+Range+";RangePeriod="+RangePeriod+";MinRangePeriod="+minRangePeriod+"\n GainTotal="+gainTotal.ToString("c");
			//SendMail("adaptive.kolebator@gmail.com", "adaptive.kolebator@gmail.com", "Backtest Tesults", s);
			log("TERMINATE "+strategyName);
		}	
		abstract protected string dumpSessionParameters();
		abstract protected string dumpState();
		protected void initLog(){
			log("**********************    "+strategyName+"   ************************************");
			log(dumpSessionParameters());
		}
		protected void logState(string line){
			string state=" :: BASE state TICK:bid="+tick.bid+";ask="+tick.ask+"  TRADE:target="+trade.target+";stop="+trade.stop+";type="+trade.type+"; POS="+getPos();
			log(line+state+" "+dumpState());
		}		
		protected void log(string line){
			string n="output";
			if(tradingLive)
				n=strategyName+"_live";
				using (System.IO.StreamWriter file = new System.IO.StreamWriter(@"C:\Users\Public\Logs\"+n+".log", true)){
					string ss="";
					if(tradingLive)
						ss="\n";
					file.WriteLine(ss+t.ToString("MM-dd HH:mm:ss")+":"+line);
				}
		}
		protected void heartbeat(){
			string n="output";
			if(tradingLive){
				n=strategyName+"_live";
				using (System.IO.StreamWriter file = new System.IO.StreamWriter(@"C:\Users\Public\Logs\"+n+".log", true)){
					file.Write(".");
				}
			}
		}

		protected int lineNum(){
			int ret=lineCount++;
			if(lineCount>5)
				lineCount=0;
			return ret;
		}
		protected override void OnMarketData(MarketDataEventArgs e){
			if(e.MarketDataType == MarketDataType.Ask||e.MarketDataType == MarketDataType.Bid){	
				tick.bid=GetCurrentBid(1);
				tick.ask=GetCurrentAsk(1);
				tickDetector();
			}
			
		}
        #region Properties
        [Description("")]
        [GridCategory("Parameters")]
        public int NumContracts
        {
            get { return numContracts; }
            set { numContracts = Math.Max(1, value); }
        }
		[Description("If set to true will not enter trades for historical ticks")]
        [GridCategory("Parameters")]
        public bool TradingLive
        {
            get { return tradingLive; }
            set { tradingLive = value; }
        }
		[Description("Specifies contract month for the instrument, like '12-13'")]
        [GridCategory("Parameters")]
        public string ContractMonth
        {
            get { return contractMonth; }
            set { contractMonth = value; }
        }
		[Description("Specifies the name of the strategy (for logging)")]
        [GridCategory("Parameters")]
        public string StrategyName
        {
            get { return strategyName; }
            set { strategyName = value; }
        }
		[Description("Specifies the name of the strategy (for logging)")]
        [GridCategory("Parameters")]
        public string InstrumentName
        {
            get { return instrumentName; }
            set { instrumentName = value; }
        }
        [Description("gainTotal - enter the account size on the start of the strategy")]
        [GridCategory("Parameters")]
        public double SimAccountSize
        {
            get { return gainTotal; }
            set { gainTotal = value; }
        }
        [Description("Minmargin")]
        [GridCategory("Parameters")]
        public double Minmargin
        {
            get { return minmargin; }
            set { minmargin = value; }
        }
        [Description("Maxmargin")]
        [GridCategory("Parameters")]
        public double Maxmargin
        {
            get { return maxmargin; }
            set { maxmargin = value; }
        }

		protected int OrderBarIndex
        {
            get { return orderBarIndex; }
            set {  orderBarIndex= value; }
        } 
        #endregion
    }
	
	
}
