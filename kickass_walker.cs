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
    [Description("Texas Ranger")]
    public class KickassWalker : Strategy
    {
		#region Types
		public struct Trade{
			public double entry;
			public double target;
			public double stop;
			public bool pending;
			public int enteredBar;
			public TRADE_TYPE type;
			public string signal;
		
		};
		public struct Range{
			public bool active;
			public double high;
			public double low;
			public double median;
			public int startBar;
			public int period;
			public int amplitude;
			public double breakoutTarget;
			public double breakoutStop;
			public bool breakout;
			public int breakoutCount;
			public int lastSwingInDir;
		};

		public struct Tick{
			public double bid;
			public double ask;
			public int	iTime;
		};
		public struct Execution{
			public IOrder entryOrder;
			public IOrder exitOrder;
			public string orderID;
			public int  pendingBar;
			public bool pendingLongEntry;
			public bool pendingShortEntry;
			public bool pendingLongExit;
			public bool pendingShortExit;
		};
		
		
		public enum RANGE_EVENT {DETECT,BREAKOUT,BREAKOUT_CLOSE,END};	//END is for unknown reason stop on loss happened
		public enum TRADE_EVENT {EXIT_ON_EOD, SWING_IN_SHORT,SWING_IN_LONG, SWING_OUT_LONG,SWING_OUT_SHORT,EXIT_LONG_LONG_TRADE,EXIT_LONG_SHORT_TRADE,EXIT_LONG,EXIT_SHORT,STOP_LONG,STOP_SHORT};
		public enum TRADE_TYPE {SWING_IN, SWING_OUT,KICKASS};
		#endregion
		
        #region Variables
        //properties
		private int     orderBarIndex=1;   																			// DataSeries 1 for all orders
		private int 	numContractrs = 1; // Default setting for NumContractrs
		private bool 	tradingLive=false; //Default setting for TradingLive
		private string 	contractMonth="12-13"; //Default setting for ContractMonth
		private string 	strategyName="RangerWalker";  //Default setting for Name
		private double 	maxRange=10;
		private double 	minRange=4;
		private int 	maxPeriod=18;
		private int 	minPeriod=3;
		private double 	profitTarget=250;
		private double 	stopLoss=400;
		private int 	timeLimitTrade=12;
		private string 	name=""; 
		private int     tm=1;	
		private bool    allowSwingOut=true;		
		private bool 	allowPreBounce=false;
		private bool	allowLongTradesKill=true;
		private bool    allowKickass=false;
		//how far past the line to go before event is triggered
		
		Trade 		trade		=	new Trade();
		Execution 	ex			=	new Execution();
		int 		bar			=	0;	
		Tick		tick		=	new Tick();
		bool 		virgin		=	true;																			//detects first live tick to reset any existing ranges
		Range		range		=	new Range();
		int 		currentBar	=	0;
		double 		gainTotal	=	0.0;																			//gain accumulator
		int 		iStartTime=20000;
		int 		iLastEntryTime=153000;
		int 		iExitOnCloseTime=155800;
		int 		iRestartTime=180000;
		double		tf=4;																							//tick fraction - how many ticks per point; 4 for QM, 1 for YM
	
		DateTime 	t;																								//current exchange time
		string 		firstInstrumentSpec="";
		int 		lineCount=0;																					//shift text lines on the chart so that they do not overlap
		
		
        #endregion
		protected override void OnStartUp()
		{
			initLog();
			
		}

		/// <summary>
		/// This method is used to configure the strategy and is called once before any strategy method is called.
		/// </summary>
		protected override void Initialize()
		{
		
			EntryHandling = EntryHandling.UniqueEntries;
			TraceOrders = false; 
			CalculateOnBarClose = true;
			IncludeCommission=true;
			BarsRequired=20;
			firstInstrumentSpec="ES "+contractMonth;
			
			
			Add(firstInstrumentSpec,PeriodType.Tick,1);																//1
			Add(firstInstrumentSpec,PeriodType.Minute,5,MarketDataType.Bid); 										//2
			Add(firstInstrumentSpec,PeriodType.Minute,5,MarketDataType.Ask); 										//3 included in live for accurate timestamps
			Add(firstInstrumentSpec,PeriodType.Tick,1,MarketDataType.Bid); 											//4 included in live for accurate timestamps
			Add(firstInstrumentSpec,PeriodType.Tick,1,MarketDataType.Ask); 											//5 included in live for accurate timestamps
			
		
			name=strategyName;
		
			SetStopLoss(stopLoss);
			/*
			SetTrailStop(stopLoss);
			SetProfitTarget(profitTarget);
			*/
		}
	
		/// <summary>
		/// Called on each bar update event (incoming tick)
		/// </summary>
		protected override void OnBarUpdate(){
		
			
						
			t=Times[1][0];																							//set time for logging
			
			/*if(tick.bid==Closes[4][0]&&tick.ask==Closes[5][0]&&(BarsInProgress==4||BarsInProgress==5))
				return;*/
			tick.bid=Closes[4][0];
			tick.ask=Closes[5][0];
			tick.iTime=ToTime(Time[0]);

			if(BarsInProgress==0){
				tick0(CurrentBar);																					//misc bar 0 processing
				if(Historical&&!tradingLive){ // when backtesting to the trades
					barDetector();
				}
				else if(!Historical&&tradingLive){
					if(virgin){
						virgin=false;
						log("RESET VIRGIN");
					}
					tick.bid=GetCurrentBid(1);
					tick.ask=GetCurrentAsk(1);
					barDetector();
				}
			}
			if(BarsInProgress==4||BarsInProgress==5||BarsInProgress==0){
				if(Historical&&!tradingLive){ // when backtesting to the trades
					tickDetector();
				}
				else if(!Historical&&tradingLive){
					if(virgin){
						virgin=false;
						log("RESET VIRGIN");
					}
					tick.bid=GetCurrentBid(1);
					tick.ask=GetCurrentAsk(1);
					tickDetector();
				}
			}
		}
		protected  void barDetector(){	
			
			int iTime=ToTime(Time[0]);	
			
			if((iTime>=iLastEntryTime&&iTime<iRestartTime)){													// detect regular trading pause TODO: get trading hours from the exchange to support short days
				range.active=false;
				return;
			}
			//saw some weird bar repetitions. This is a better safe and than sorry check:
			if(CurrentBar==currentBar)
				return;
			currentBar=CurrentBar;
			
			#region Range Chart Drawing
			if(range.active){																					// delayed drawing of the range lines on a 5min tick
				int startingBar=bar-range.startBar+range.period;
				DrawLine("uwline"+range.startBar,true,startingBar,range.high,1,
					range.high,Color.DarkOliveGreen,DashStyle.Solid,1);
				DrawLine("dwline"+range.startBar,true,startingBar,range.low,1,
					range.low,Color.DarkBlue,DashStyle.Solid,1);
				DrawLine("mline"+ range.startBar,true,startingBar,range.median,1,
					range.median,Color.DarkGray,DashStyle.Dash,1);
				DrawText( "t"+range.startBar,true,"R:"+range.amplitude+"  Total:"
					+gainTotal.ToString("c"),bar
					-range.startBar+range.period,range.high+lineNum()*0.25,20,Color.Black, 
					new Font("Ariel",8),StringAlignment.Near,Color.Transparent,Color.Beige, 0);
			}
			#endregion
			int pos= getPos();
			
			if(bar>maxPeriod&&!range.active){
				//range detection
				
				int r=maxPeriod+1;
				while(r>minPeriod){
					r--;
					double mx=Math.Max(MAX(Closes[3],r)[0],MAX(Opens[3],r)[0]);									// high-end of the body (narrow) range
					double mn=Math.Min(MIN(Closes[2],r)[0],MIN(Opens[2],r)[0]); 								// low-end of the body range
					double mxh=MAX(Highs[3],r)[3];            													// high end of the full (wide) range 
					double mnh=MIN(Lows[2],r)[2];																// low-end of the wide range
					
					double wr=mxh-mnh;																			// wide range, high to lows
					double nr=mx-mn;										    								// narrow range, for bodies only	
					if(wr<=maxRange/4&&wr>=minRange/4){								   							// max range					
									
						double rm=mnh+wr/2;																		// range median
						
						//accumullators
						double sbup=0, sbdn=0, sbh=0, sbl=0,sbm=0;
						
						//is first half a range on its own	
						int hrp=r/2;
						int count=0;
						bool touchedTop=false;																	// at least one bar in this half touched top
						bool touchedBottom=false;																// at least one bar in this half touched bottom
											
						for(int i=0;i<hrp;i++){
							double top=Math.Max(Opens[2][i],Closes[2][i]);
							double btm=Math.Min(Opens[3][i],Closes[3][i]);
							sbup+=top;       																	// high-end of the candle body
							sbdn+=btm;       																	// low-end of the candle body
							if(top>=mx&&btm<=rm)
								touchedTop=true;
							if(btm<=mn&&top>=rm)
								touchedBottom=true;
							count++;
						}
						//averages:
						double abup=sbup/count;
						double abdn=sbdn/count;
						
						if((abdn<rm&&abup>rm)/*&&touchedTop&&touchedBottom*/){
							//second half resets:
							sbup=0;	sbdn=0;	sbh=0;	sbl=0;
							count=0;
							touchedTop=false;
							touchedBottom=false;
							
							for(int i=hrp;i<r;i++){
								double top=Math.Max(Opens[2][i],Closes[2][i]);
								double btm=Math.Min(Opens[3][i],Closes[3][i]);
								sbup+=top;      		 														// high-end of the candle body
								sbdn+=btm;       																// low-end of the candle body
								if(top>=mx&&btm<=rm)
									touchedTop=true;
								if(btm<=mn&&top>=rm)
									touchedBottom=true;
								//log("TRACE sbdn["+i+"]+"+Math.Min(Opens[3][i],Closes[3][i]));
								//sbh+=Highs[3][i];																// high
								//sbl+=Lows[2][i];																// low
								count++;
							}
							//averages:
							abup=sbup/count;
							abdn=sbdn/count;
							if((abdn<rm&&abup>rm)){								// range detected
								double er=(wr*4);
								
								if(nr*4<minRange||er>maxRange||nr>wr)
									continue;
								//verify that the data is good:
								int lastBid=ToTime(Times[4][0]);
								int lastAsk=ToTime(Times[5][0]);
								if((ToTime(Time[0])-lastBid<60)&&ToTime(Time[0])-lastAsk<60){					//gap less than 60 secs
									//Starting a new range:
									double tgt=(1.0/(er));//+2;;
									
									tgt=(tgt*tgt*4*r*(wr-nr)*3+hrp);
									//tgt=Math.Round(tgt,0);
									tgt=(int)tgt;
									//tgt=5;
									//log("proposed tgt="+tgt);
									if(tgt>=2){
									/************************************************************************/
									//  RANGE START
										//bounceTriggered=false;													//reset secondary (bounce) watch
										//breakRange=false;														//reset hard break of the range indicator
										//noEntry=false;
										range.active=true;																//set watch indicator
										range.high=mxh;
										range.low=mnh;
										range.median=mnh+(mxh-mnh)/2;
										range.amplitude=(int)er;
										range.breakoutTarget=Math.Round(tgt,0);
										range.lastSwingInDir=0;
										//preBounce=false;
										//target=Math.Max(target,2);
										range.startBar=bar;
										range.period=r;
										range.breakout=false;
										range.breakoutCount=0;
										//logState("RANGE DETECTED");
										/*log("START WATCH target="+target+";range="+enteredRange+";rangePeriod="
											+enteredPeriod+";WATCH UP="+watch_up+";DOWN="+watch_down+";bounceTriggered="
											+bounceTriggered+";breakRange="+breakRange);*/
										DrawDiamond("dm"+CurrentBars[1],true,0,range.high+0.25,Color.Blue);
										if(gainTotal>0){
											DrawText( "tm2"+CurrentBars[1],true,"TOTAL: "+gainTotal.ToString("c") ,
											0,range.high+lineNum()*0.25,20,Color.Green, new Font("Ariel",8),
											StringAlignment.Near,Color.Transparent,Color.Beige, 0);
										}
										else{
											DrawText( "tm2"+CurrentBars[1],true,"TOTAL: "+gainTotal.ToString("c") ,
											0,range.high+lineNum()*0.25,20,Color.Red, new Font("Ariel",8),
											StringAlignment.Near,Color.Transparent,Color.Beige, 0);
										}
										processRangeEvent(RANGE_EVENT.DETECT);
									/************************************************************************/											
									}
								break;
								}
							}
						}
					}
				}
			}
			
			else if(range.active&&(tick.bid>range.high||tick.ask<range.low)){
				if(range.breakout||range.breakoutCount>2){
					range.active=false;
					processRangeEvent(RANGE_EVENT.BREAKOUT);
				}
				else{
					range.breakoutCount++;
				}
			}
			
			if(pos!=0&&bar-trade.enteredBar>timeLimitTrade){
				logState("LONG TRADE DETECTED bar="+bar+";tradeBar="+trade.enteredBar);
				if((pos>0&&tick.bid>trade.entry)||(pos<0&&tick.ask<trade.entry)){
					//log("POSITIVE OUT");
					if(pos>0)
						processTradeEvent(TRADE_EVENT.EXIT_LONG_LONG_TRADE);
					else
						processTradeEvent(TRADE_EVENT.EXIT_LONG_SHORT_TRADE);
				}
			}
			
			if(pos==0&&allowKickass){
				double prevCandleHeight=High[1]-Low[1];
				double down_tip=High[1]-prevCandleHeight/4;
				double up_tip=Low[1]+prevCandleHeight/4;
				double candleHeight=High[0]-Low[0];
				double prevCandleBottom=Math.Min(Open[1],Close[1]);
				double prevCandleTop=Math.Max(Open[1],Close[1]);
				double prevCandleBody=prevCandleTop-prevCandleBottom;
				double body_up_tip=prevCandleBottom+prevCandleBody/3;
				double body_down_tip=prevCandleTop-prevCandleBody/3;
				int prevCandleDir;
				int candleDir;
				if(Open[1]>Close[1])
					prevCandleDir=-1;
				else
					prevCandleDir=1;
				
				if(Open[0]>Close[0])
					candleDir=-1;
				else
					candleDir=1;
				
				if(tradingLive){
					logState("HEARTBEAT: prevCandleDir="+prevCandleDir+";candleDir="+candleDir+";prevCandleHeight="+prevCandleHeight+";down_tip="+down_tip+";up_tip="+up_tip+";Close[0]="+Close[0]+";Math.Min(MIN(Opens[2],3)[2],MIN(Closes[2],3)[2])="+Math.Min(MIN(Opens[2],3)[2],MIN(Closes[2],3)[2]));
				}
				log("DEBUG prevCandleDir="+prevCandleDir+";candleDir="+candleDir+";candleHeight="+candleHeight+";prevCandleBody="+prevCandleBody+";prevCandleTop="+prevCandleTop+";prevCandleBottom="+prevCandleBottom+";body_up_tip="+body_up_tip+";body_down_tip="+body_down_tip+";prevCandleHeight="+prevCandleHeight+";down_tip="+down_tip+";up_tip="+up_tip+";Close[0]="+Close[0]+";Math.Min(MIN(Opens[2],3)[2],MIN(Closes[2],3)[2])="+Math.Min(MIN(Opens[2],3)[2],MIN(Closes[2],3)[2]));
				
				bool bPrevdirRed=prevCandleDir==-1;
				bool bMydirGreen=candleDir==1;
				bool bPrevdirGreen=prevCandleDir==1;
				bool bMydirRed=candleDir==-1;
				bool bCandleHeight=candleHeight>=1.0;
				bool bPrevCanldeHeight=prevCandleHeight>=2.0;
				bool bPrevCandlesAboveTheBodyDownTip=body_down_tip<=Math.Min(MIN(Opens[0],6)[2],MIN(Closes[0],6)[2]);
				bool bPrevCandlesBelowTheBodyUpTip=body_up_tip>=Math.Max(MAX(Opens[0],6)[2],MAX(Closes[0],6)[2]);
				bool bCloseAboveDownTip=Close[0]>=down_tip;
				bool bCloseBelowUpTip=Close[0]<=up_tip;
				bool bCloseAboveBodyDownTip=Close[0]>=body_down_tip;
				bool bCloseBelowBodyUpTip=Close[0]<=body_up_tip;
				string conds="bPrevdirRed="+bPrevdirRed+";bMydirGreen="+bMydirGreen+";bCandleHeight="+bCandleHeight+";bPrevCanldeHeight="+bPrevCanldeHeight+";bPrevCandlesAboveTheBodyDownTip="+bPrevCandlesAboveTheBodyDownTip+
				";bPrevCandlesBelowTheBodyUpTip="+bPrevCandlesBelowTheBodyUpTip+";bCloseAboveDownTip="+bCloseAboveDownTip+";bCloseBelowUpTip="+bCloseBelowUpTip+";bCloseAboveBodyDownTip="+bCloseAboveBodyDownTip+";bCloseBelowBodyUpTip="+bCloseBelowBodyUpTip+";bCloseBelowBodyUpTip="+bCloseBelowBodyUpTip;
				log("DEBUG CONDS:" +conds);
				if(bPrevdirRed&&bMydirGreen&&bCandleHeight&&bPrevCanldeHeight&&bPrevCandlesAboveTheBodyDownTip&&bCloseAboveDownTip&&bCloseAboveBodyDownTip){
					trade.type=TRADE_TYPE.KICKASS;
					//trade.target=Math.Round(prevCandleHeight*4*4);
					//trade.stop=Math.Round(prevCandleHeight*4*3);
					if(prevCandleHeight>3){
						trade.target=12;
						trade.stop=12;
					}
					else{
						trade.target=4;
						trade.stop=10;
					}
					trade.signal="LongKickass";
					enterLong(tick.ask);
					logState("ENTER LONG KIKASS");
					//DrawDiamond("ka"+CurrentBars[0],true,0,tick.bid-45,Color.Green);
				}
				if(bPrevdirGreen&&bMydirRed&&bCandleHeight&&bPrevCanldeHeight&&bPrevCandlesBelowTheBodyUpTip&&bCloseBelowUpTip&&bCloseBelowBodyUpTip){
					trade.type=TRADE_TYPE.KICKASS;
					//trade.target=Math.Round(prevCandleHeight*4*4);
					//trade.stop=Math.Round(prevCandleHeight*4*3);
					
					if(prevCandleHeight>3){
						trade.target=12;
						trade.stop=12;
					}
					else{
						trade.target=4;
						trade.stop=10;
					}
					trade.signal="ShortKickass";
					enterShort(tick.bid);
					logState("ENTER SHORT KIKASS");
					//DrawDiamond("ka"+CurrentBars[0],true,0,tick.ask+4,Color.Red);
				}
			}
			
		}
		protected  void processRangeEvent(RANGE_EVENT e){
		logState("RANGE EVENT "+e);
			/*switch(e){
				case RANGE_EVENT.DETECT:
				case RANGE_EVENT.BREAKOUT:
			}*/
		}
		protected  void processTradeEvent(TRADE_EVENT e){
		
		switch(e){
				case TRADE_EVENT.EXIT_ON_EOD:
					if(getPos()>0)
						exitLongMarket();
					else
						exitShortMarket();
					logState("TRADE EVENT "+e);	
					break;
				case TRADE_EVENT.SWING_OUT_LONG:
					trade.type=TRADE_TYPE.SWING_OUT;
					trade.target=range.breakoutTarget;
					trade.stop=(tick.ask-range.low)*tf+tm*2;
					trade.signal="LongSwingOut";
					enterLongMarket();
					//enterLong(tick.bid);
					logState("TRADE EVENT "+e);	
					break;
				case TRADE_EVENT.SWING_OUT_SHORT:
					trade.type=TRADE_TYPE.SWING_OUT;
					trade.target=range.breakoutTarget;
					trade.stop=(range.high-tick.bid)*tf+tm*2;
					trade.signal="ShortSwingOut";
					enterShortMarket();
					//enterShort(tick.ask);
					logState("TRADE EVENT "+e);	
					break;
				case TRADE_EVENT.SWING_IN_LONG:
					if(range.lastSwingInDir<=0){
						trade.type=TRADE_TYPE.SWING_IN;
						//double t=Math.Max((range.high-range.median)/4,0.25);
						double t=0.25;
						trade.target=(int)(Math.Round(range.median-tick.bid+t,0)*tf);
						//if(tick.bid+trade.target/tf>=range.high)
						//	trade.target=(int)(range.high-tick.bid)*tf-tm;
						trade.target=2;Math.Max(trade.target,tm);
						trade.stop=(int)((tick.bid-range.low)*tf+tm+2);
						enterLong(tick.bid);	
						range.lastSwingInDir=1;
						logState("TRADE EVENT "+e);	
					}
					break;	
				case TRADE_EVENT.SWING_IN_SHORT:
					if(range.lastSwingInDir<=0){
						trade.type=TRADE_TYPE.SWING_IN;
						//logState("DEBUG: tick.ask-range.median="+(tick.ask-range.median)+";tgt="+((tick.ask-range.median)*tf)+";target="+((tick.ask-range.median)*tf+tm));
						
						//double t=Math.Max((range.median-range.low)/4,0.25);
						double t=0.25;
						trade.target=(int)(Math.Round(tick.ask-range.median+t,0)*tf);
						//if(tick.ask-trade.target/tf<=range.low)
						//	trade.target=(int)(tick.ask-range.low)*tf-tm;
						trade.target=2;//Math.Max(trade.target,tm);
						trade.stop=(int)((range.high-tick.ask)*tf+tm+2);
						enterShort(tick.ask);	
						range.lastSwingInDir=-1;
						logState("TRADE EVENT "+e);	
					}
					break;	
				case TRADE_EVENT.EXIT_LONG_LONG_TRADE:
				case TRADE_EVENT.EXIT_LONG:
					
				case TRADE_EVENT.STOP_LONG:
					exitLongMarket();
					logState("TRADE EVENT "+e);	
					break;
				case TRADE_EVENT.EXIT_LONG_SHORT_TRADE:
				case TRADE_EVENT.EXIT_SHORT:
					
				case TRADE_EVENT.STOP_SHORT:
					exitShortMarket();
					logState("TRADE EVENT "+e);	
					break;
					
			}
		}
		protected  void tickDetector(){
			int iTime=ToTime(Time[0]);
			if(trade.pending)
				return;
			int pos=getPos();
			if(pos!=0) {
						
				if(iTime>=iExitOnCloseTime&&iTime<iRestartTime){
					processTradeEvent(TRADE_EVENT.EXIT_ON_EOD);
					return;
				}
			}
			if(pos==0&&(iTime>=iLastEntryTime&&iTime<iRestartTime)){
				return;
			}
			if(range.active){
				if(pos==0/*&&(iTime<83000||iTime>100500)*/){
					if(allowSwingOut&&tick.bid>range.high+tm/tf){
						if(!range.breakout)
							processTradeEvent(TRADE_EVENT.SWING_OUT_LONG);
						range.breakout=true;
					}
					else if(allowSwingOut&&tick.ask<range.low-tm/tf){
						if(!range.breakout)
							processTradeEvent(TRADE_EVENT.SWING_OUT_SHORT);
						range.breakout=true;
					}
					else
					if(allowPreBounce&&tick.bid>range.median&&tick.ask<=range.median+0.25){
						processTradeEvent(TRADE_EVENT.SWING_IN_SHORT);
					}
					else if(allowPreBounce&&tick.ask<range.median&&tick.bid>=range.median-0.25){
						processTradeEvent(TRADE_EVENT.SWING_IN_LONG);
					}
				}
			}
			if(pos>0){
				
				if(tick.bid>=trade.entry+trade.target/tf){
					processTradeEvent(TRADE_EVENT.EXIT_LONG);
				}
				else if(tick.bid<=trade.entry-trade.stop/tf){
					processTradeEvent(TRADE_EVENT.STOP_LONG);
				}
				else if(trade.type==TRADE_TYPE.SWING_OUT&&tick.ask<range.high-tm/tf&&tick.ask<trade.entry-2*tm/tf){
					log("SWING OUT BRAKE");
					processTradeEvent(TRADE_EVENT.EXIT_LONG);
				}
			}
			else if(pos<0){
				if(tick.ask<=trade.entry-trade.target/tf){
					processTradeEvent(TRADE_EVENT.EXIT_SHORT);
				}
				else if(tick.ask>=trade.entry+trade.stop/tf){
					processTradeEvent(TRADE_EVENT.STOP_SHORT);
				}
				else if(trade.type==TRADE_TYPE.SWING_OUT&&tick.bid>range.low+tm/tf&&tick.bid>trade.entry+2*tm/tf){
					log("SWING OUT BRAKE");
					processTradeEvent(TRADE_EVENT.EXIT_SHORT);
				}
			}
		}
		
		
		/*************************************************
		EXECUTION
		**************************************************/
			
		// only called on bar[0]
		void tick0(int bar){
			this.bar=bar;
			
			if(bar>ex.pendingBar+1){																		//taking care of stuck orders
				if(ex.pendingLongEntry||ex.pendingShortEntry){
					if(ex.entryOrder!=null){
						CancelOrder(ex.entryOrder);
						log("<<< CANCELLING ENTRY ORDER >>> pendingBar="+ex.pendingBar
							+";CurrentBar"+CurrentBar+";ask="+tick.ask+";bid="+tick.bid);
					}
				}
				else if(ex.pendingLongExit||ex.pendingShortExit){
					if(ex.exitOrder!=null){
						CancelOrder(ex.exitOrder);
						log(">>> CANCELLING EXIT ORDER <<< pendingBar="+ex.pendingBar
							+";CurrentBar"+CurrentBar+";ask="+tick.ask+";bid="+tick.bid);
					}
				}
			}
		}
		
		void enterLong(double limit){
			trade.pending=true;
			ex.pendingLongEntry=true;
			ex.pendingBar=bar;
			ex.orderID=trade.signal;
			ex.entryOrder=EnterLongLimit(OrderBarIndex,true,NumContractrs,limit,ex.orderID);
		}
		void enterLongMarket(){
			trade.pending=true;
			ex.pendingLongEntry=true;
			ex.pendingBar=bar;
			ex.orderID=trade.signal;
			ex.entryOrder=EnterLong(OrderBarIndex,NumContractrs,ex.orderID);
		}
		void enterShort( double limit){
			trade.pending=true;
			ex.pendingShortEntry=true;
			ex.pendingBar=bar;
			ex.orderID=trade.signal;
			ex.entryOrder=EnterShortLimit(OrderBarIndex,true,NumContractrs,limit,ex.orderID);
		}
		void enterShortMarket(){
			trade.pending=true;
			ex.pendingShortEntry=true;
			ex.pendingBar=bar;
			ex.orderID=trade.signal;
			ex.entryOrder=EnterShort(OrderBarIndex,NumContractrs,ex.orderID);
		}
		void exitShort(double limit){
			trade.pending=true;
			ex.pendingShortExit=true;
			ex.pendingBar=bar;
			ex.exitOrder=ExitShortLimit(OrderBarIndex,true,NumContractrs,limit,ex.orderID,ex.orderID);
		}
		void exitShortMarket(){
			trade.pending=true;
			ex.pendingShortExit=true;
			ex.pendingBar=bar;
			ex.exitOrder=ExitShort(OrderBarIndex,NumContractrs,ex.orderID,ex.orderID);
		}
		void exitLong(double limit){
			trade.pending=true;
			ex.pendingLongExit=true;
			ex.pendingBar=bar;
			ex.exitOrder=ExitLongLimit(OrderBarIndex,true,NumContractrs,limit,ex.orderID,ex.orderID);
		}
		void exitLongMarket(){
			trade.pending=true;
			ex.pendingLongExit=true;
			ex.pendingBar=bar;
			ex.exitOrder=ExitLong(OrderBarIndex,NumContractrs,ex.orderID,ex.orderID);
		}
		int getPos(){
			Position pES=Positions[OrderBarIndex];
			if(pES.MarketPosition==MarketPosition.Long)
				return 1;
			if(pES.MarketPosition==MarketPosition.Short)
				return -1;
			return 0;
		}
	
	
		protected override void OnOrderUpdate(IOrder order){
    	if (ex.entryOrder != null && ex.entryOrder == order){
         //Print(order.ToString());
			if (order.OrderState == OrderState.Cancelled){
				log("---->>>> Entry Order Cancelled");
				ex.pendingLongEntry=false;
				ex.pendingShortEntry=false;
				trade.pending=false;
				
				ex.entryOrder = null;
			 }
    	}
		else if (ex.exitOrder != null && ex.exitOrder == order){
         //Print(order.ToString());
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
			if(trade.pending){
				if (Positions[OrderBarIndex].MarketPosition == MarketPosition.Flat){
					if(ex.pendingLongExit||ex.pendingShortExit){
						logState("EXITED FLAT");
						trade.pending=false;
						ex.pendingLongExit=false;
						ex.pendingShortExit=false;
						trade.entry=0;
					}
				}
				if(ex.pendingLongEntry){
					if (Positions[OrderBarIndex].MarketPosition == MarketPosition.Long){
						logState("POSITION: ENTERED LONG");
						trade.pending=false;
						ex.pendingLongEntry=false;
					}
				}
				else if(ex.pendingShortEntry){
					if (Positions[OrderBarIndex].MarketPosition == MarketPosition.Short){
						logState("POSITION: ENTERED SHORT");
						trade.pending=false;
						ex.pendingShortEntry=false;
					}
				}
			}
		}
		protected override void OnExecution(IExecution execution){
			if (execution.Order != null && execution.Order.OrderState == OrderState.Filled){
				//log("Execution:"+execution.ToString());
				if (ex.entryOrder != null && ex.entryOrder == execution.Order){
					trade.entry=execution.Order.AvgFillPrice;
					trade.enteredBar=bar;
					log("EXECUTION: ENTERED at "+trade.entry);
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
					double net_gain=gain*4*12.5-4;
					
					gainTotal+=net_gain;
					log("EXITED at "+currentExit+";$$$$$$ gain="+gain+";net="+net_gain.ToString("C")+"; $$$$$ total="+gainTotal.ToString("C"));
				
					if(ex.exitOrder==null){
						log("EXECUTION: RESET WATCH");
						processRangeEvent(RANGE_EVENT.END);
						range.active=false;
					}
					ex.exitOrder=null;		
				}	
			}
		}
		protected override void OnTermination(){
  			string s="hello";
			//string s="Finished Simulated Session "+strategyName+"  "+ToDay(firstTime)+"-"+ToDay(t)+"\n 	Range="+Range+";RangePeriod="+RangePeriod+";MinRangePeriod="+minRangePeriod+"\n GainTotal="+gainTotal.ToString("c");
			SendMail("adaptive.kolebator@gmail.com", "adaptive.kolebator@gmail.com", "Backtest Tesults", s);
			log("SENT MAIL");
		}	
		
		protected void initLog(){
			
			log("**********************    "+name+"   ************************************");
			log("MaxRange="+maxRange+";MaxRangePeriod="+MaxRangePeriod+";MinRange="+minRange+";MinRangePeriod="+MinRangePeriod);
		}
		protected void logState(string line){
			
			string state=" :: RANGE:amplitude="+range.amplitude+";period="+range.period+";high="+range.high+";median="+range.median+";low="+range.low+";active="+range.active+"  TICK:bid="+tick.bid+";ask="+tick.ask+"  TRADE:target="+trade.target+";stop="+trade.stop+";type="+trade.type+"; POS="+getPos();
			log(line+state);
		}		
		protected void log(string line){
			string n="output";
			if(tradingLive)
				n=name+"_live";
			using (System.IO.StreamWriter file = new System.IO.StreamWriter(@"C:\Users\Public\Logs\"+n+".log", true))
			{
				file.WriteLine(t.ToString("MM-dd HH:mm:ss")+":"+line);
			}
		}
		protected int lineNum(){
			int ret=lineCount++;
			if(lineCount>5)
				lineCount=0;
			return ret;
		}
        #region Properties
       /* [Description("")]
        [GridCategory("Parameters")]
        public int Bottom
        {
            get { return bottom; }
            set { bottom = Math.Max(1, value); }
        }

        [Description("")]
        [GridCategory("Parameters")]
        public int Top
        {
            get { return top; }
            set { top = Math.Max(1, value); }
        }*/

        [Description("")]
        [GridCategory("Parameters")]
        public int NumContractrs
        {
            get { return numContractrs; }
            set { numContractrs = Math.Max(1, value); }
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
		[Description("")]
        [GridCategory("Parameters")]
        public int MaxRangePeriod
        {
            get { return maxPeriod; }
            set { maxPeriod = value; }
        } 
		[Description("Maximum Range")]
        [GridCategory("Parameters")]
        public double MaxRange
        {
            get { return maxRange; }
            set { maxRange = value; }
        } 
/*
		[Description("")]
        [GridCategory("Parameters")]
        public double ProfitTarget
        {
            get { return profitTarget; }
            set { profitTarget = value; }
        }*/ 
		[Description("")]
        [GridCategory("Parameters")]
        public double StopLoss
        {
            get { return stopLoss; }
            set { stopLoss = value; }
        } 
		[Description("")]
        [GridCategory("Parameters")]
        public int MinRangePeriod
        {
            get { return minPeriod; }
            set { minPeriod = value; }
        } 
		[Description("")]
        [GridCategory("Parameters")]
        public double MinRange
        {
            get { return minRange; }
            set { minRange= value; }
        } 
		
		[Description("Exit the positive trades after this number of bars")]
        [GridCategory("Parameters")]
        public int TimeLimitTrade
        {
            get { return timeLimitTrade; }
            set {  timeLimitTrade= value; }
        } 
		[Description("Enables oscillating before the breakout")]
        [GridCategory("Parameters")]
        public bool AllowSwingIn
        {
            get { return allowPreBounce; }
            set {  allowPreBounce= value; }
        } 
		[Description("Enables oscillating before the breakout")]
        [GridCategory("Parameters")]
        public bool AllowSwingOut
        {
            get { return allowSwingOut; }
            set {  allowSwingOut= value; }
        }
		[Description("Enables terminating long trades")]
        [GridCategory("Parameters")]
        public bool AllowLongTradesKill
        {
            get { return allowLongTradesKill; }
            set {  allowLongTradesKill= value; }
        }
		[Description("Enables allowKickass indicator")]
        [GridCategory("Parameters")]
        public bool AllowKickass
        {
            get { return allowKickass; }
            set {  allowKickass= value; }
        }
		protected int OrderBarIndex
        {
            get { return orderBarIndex; }
            set {  orderBarIndex= value; }
        } 
        #endregion
    }
	
	
}
