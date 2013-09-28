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
    /// Manual range trading
    /// </summary>
    [Description("Texas Ranger")]
    public class Walker : Strategy
    {
        #region Variables
        //properties
		private int numContractrs = 1; // Default setting for NumContractrs
		private bool tradingLive=false; //Default setting for TradingLive
		private string contractMonth="12-13"; //Default setting for ContractMonth
		private string strategyName="Walker";  //Default setting for Name
		private double range=10;
		private double minRange=4;
		private int rangePeriod=36;
		private int minRangePeriod=3;
		private double profitTarget=250;
		private double stopLoss=400;
		private double k=1.5;
		private int timeLimitTrade=12;
		
		//trade params
		private double stop = 0;
		private double target = 0;
		int dir=0;
		
		private string name=""; 
		
		
		double gainTotal=0;																									//gain accumulator
	
		// position management
		private bool pendingPosition=false;																					//
		private bool pendingLongEntry=false;
		private bool pendingShortEntry=false;
		private bool pendingLongExit=false;
		private bool pendingShortExit=false;
		
		private double currentEntry=0;
		private string dayStartTime = "2:00:00 AM";
		private string lastEntryTime = "3:30:00 PM";
		private string exitOnCloseTime = "3:58:00 PM";
		private int iStartTime=20000;
		private int iLastEntryTime=153000;
		private int iExitOnCloseTime=155800;
		private int iRestartTime=180000;
		
		private bool active=false;
		
		private bool virgin=true;
		private string firstInstrumentSpec="";
		private string secondInstrumentSpec="";
		
		private DateTime t;																									//current exchange time
		bool sell=false;
		bool trade_active=false;
		
		string orderID;
		
		//watch:
		bool watch=false;
		double watch_up=0;
		double watch_down=0;
		double watch_median=0;
		bool bounceTriggered=false;																							//goes in effect on reversal to get into oscillating (bouncing) mode within the range
		int innerDir=0;
		int innerTarget;
		int innerStop;
		bool breakRange=false;																							//does not immediately end watches but will prevent next outer reversal
		bool outerTrade=false;
		double delayedTarget=0;
		double delayedStop=0;
		//additional range state
		int rangeStartBar=0;
		int enteredBar=0;
		double enteredHeight=0.0;
		double enteredRange=0;
		int enteredPeriod=0;
		
		private IOrder entryOrder = null;
		private IOrder exitOrder = null;
		int pendingBar=0;		
		int currentBar=0;
		bool drawRange=false;
		int lineCount=0;																								//shift text lines on the chart so that they do not overlap
		
		DateTime firstTime;
		bool trailStop=false;
		bool noEntry=false;
		bool preBounce=false;
		
		double thebid=0;
		double theask=0;
		bool allowPreBounce=true;
		
		//Stack<int> spreadAsks=new Stack<int>();
        // User defined variables (add any user defined variables below)
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
			secondInstrumentSpec="FESX "+contractMonth;
			
			Add(firstInstrumentSpec,PeriodType.Tick,1);			//1
			Add(firstInstrumentSpec,PeriodType.Minute,5,MarketDataType.Ask); 										//2 included in live for accurate timestamps
			Add(firstInstrumentSpec,PeriodType.Minute,5,MarketDataType.Bid); 										//3
			Add(firstInstrumentSpec,PeriodType.Tick,1,MarketDataType.Ask); 											//4 included in live for accurate timestamps
			Add(firstInstrumentSpec,PeriodType.Tick,1,MarketDataType.Bid); 											//5 included in live for accurate timestamps
		
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
		//	if(BarsInProgress==2 ||BarsInProgress==3||BarsInProgress==4||BarsInProgress==5)
		//		return;
			if(BarsInProgress==4){
				if(Closes[4][0].Equals(theask))
					return;
				theask=Closes[4][0];
			}				
			
			if(BarsInProgress==5){
				if(Closes[5][0].Equals(thebid))
					return;
				thebid=Closes[5][0];
			}				
			t=Times[1][0];
			
			if(active==false){
				if (CurrentBars[1] < 1)
					return;
				firstTime=t;
				active=true;
			}
			trade_active=false;
			
			double bid=Closes[5][0];
			double ask=Closes[4][0];
			
			
			
			if(BarsInProgress==0){																					// only on 5 minute bars
				int iTime=ToTime(Time[0]);	
				if((iTime>=iLastEntryTime&&iTime<iRestartTime)){													// detect regular trading pause TODO: get trading hours from the exchange to support short days
					watch=false;
					bounceTriggered=false;
					return;
				}
				//saw some weird bar repetitions. This is a better safe and than sorry check:
				if(CurrentBar==currentBar)
					return;
				currentBar=CurrentBar;
				
				#region Range Chart Drawing
				if(drawRange||watch){																						// delayed drawing of the range lines on a 5min tick
					int startingBar=CurrentBars[0]-rangeStartBar+enteredPeriod;
					DrawLine("uwline"+rangeStartBar,true,startingBar,watch_up,1,
						watch_up,Color.DarkOliveGreen,DashStyle.Solid,1);
					DrawLine("dwline"+rangeStartBar,true,startingBar,watch_down,1,
						watch_down,Color.DarkBlue,DashStyle.Solid,1);
					DrawLine("mline"+ rangeStartBar,true,startingBar,watch_median,1,
						watch_median,Color.DarkGray,DashStyle.Dash,1);
					DrawText( "t"+rangeStartBar,true,"R:"+enteredRange+"  Total:"
						+gainTotal.ToString("c"),CurrentBars[0]
						-rangeStartBar+enteredPeriod,watch_up+lineNum()*0.25,20,Color.Black, 
						new Font("Ariel",8),StringAlignment.Near,Color.Transparent,Color.Beige, 0);
					drawRange=false;	
				}
				#endregion
				int pos= dirOfPosition();
				
				if(CurrentBars[0]>rangePeriod&&!pendingPosition&&!watch){
					//range detection
					
					trade_active=false;
					int r=rangePeriod+1;
					while(r>minRangePeriod){
						r--;
						double mx=Math.Max(MAX(Closes[2],r)[0],MAX(Opens[2],r)[0]);									// high-end of the body (narrow) range
						double mn=Math.Min(MIN(Closes[3],r)[0],MIN(Opens[3],r)[0]); 								// low-end of the body range
						double mxh=MAX(Highs[2],r)[3];            													// high end of the full (wide) range 
						double mnh=MIN(Lows[3],r)[2];																// low-end of the wide range
						
						double wr=mxh-mnh;																			// wide range, high to lows
						double nr=mx-mn;										    								// narrow range, for bodies only	
						if(wr<=range/4&&wr>=minRange/4){								   							// max range					
										
							double rm=mnh+wr/2;																		// range median
							
							//accumullators
							double sbup=0, sbdn=0, sbh=0, sbl=0,sbm=0;
							
							//is first half a range on its own	
							int hrp=r/2;
							int count=0;
							bool touchedTop=false;																	// at least one bar in this half touched top
							bool touchedBottom=false;																// at least one bar in this half touched bottom
												
							for(int i=0;i<hrp;i++){
								double top=Math.Max(Opens[3][i],Closes[3][i]);
								double btm=Math.Min(Opens[2][i],Closes[2][i]);
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
									double top=Math.Max(Opens[3][i],Closes[3][i]);
									double btm=Math.Min(Opens[2][i],Closes[2][i]);
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
									int er=(int)wr*4;
									
									if(nr*4<minRange||er>range||nr>wr)
										continue;
									//verify that the data is good:
									int lastBid=ToTime(Times[5][0]);
									int lastAsk=ToTime(Times[4][0]);
									if((ToTime(Time[0])-lastBid<60)&&ToTime(Time[0])-lastAsk<60){					//gap less than 60 secs
										//Starting a new range:
										double tgt=(1.0/((double)er));//+2;;
										
										tgt=(tgt*tgt*4*enteredPeriod*(wr-nr)*3+hrp);
										log("proposed tgt="+tgt);
										if(tgt>=2){
										/************************************************************************/
										//  RANGE START
											bounceTriggered=false;													//reset secondary (bounce) watch
											breakRange=false;														//reset hard break of the range indicator
											noEntry=false;
											watch=true;																//set watch indicator
											watch_up=mxh;
											watch_down=mnh;
											watch_median=mnh+(mxh-mnh)/2;
											enteredRange=er;
											target=(int)tgt;
											preBounce=false;
											//target=Math.Max(target,2);
											rangeStartBar=CurrentBars[0];
											enteredPeriod=r;
											log("START WATCH target="+target+";range="+enteredRange+";rangePeriod="
												+enteredPeriod+";WATCH UP="+watch_up+";DOWN="+watch_down+";bounceTriggered="
												+bounceTriggered+";breakRange="+breakRange);
											DrawDiamond("dm"+CurrentBars[1],true,0,watch_up+0.25,Color.Blue);
											if(gainTotal>0){
												DrawText( "tm2"+CurrentBars[1],true,"TOTAL: "+gainTotal.ToString("c") ,
												0,watch_up+lineNum()*0.25,20,Color.Green, new Font("Ariel",8),
												StringAlignment.Near,Color.Transparent,Color.Beige, 0);
											}
											else{
												DrawText( "tm2"+CurrentBars[1],true,"TOTAL: "+gainTotal.ToString("c") ,
												0,watch_up+lineNum()*0.25,20,Color.Red, new Font("Ariel",8),
												StringAlignment.Near,Color.Transparent,Color.Beige, 0);
											}
											drawRange=true;
										/************************************************************************/											
										}
									break;
									
									}
								}
							}
						}
					}
				}
				//if(watch&&!trade_active)
				//	log("$$$ WATCH UP="+watch_up+";DOWN="+watch_down+";bounceTriggered="+bounceTriggered+";breakRange="+breakRange);E
				//(!watch)
				//oEntry=true;
			}
			if(BarsInProgress==4||BarsInProgress==5||BarsInProgress==0){
				if(Historical&&!tradingLive){ // when backtesting to the trades
					processTick(theask,thebid);
				}
				else if(!Historical&&tradingLive){
					if(virgin){
						virgin=false;
						watch=false;
						log("RESET VIRGIN WATCH");
					}
					processTick(GetCurrentAsk(1),GetCurrentBid(1));
				}
			}
		}
		protected void processTick(double ask,double bid){
			
			if(ToTime(Time[0])==0){
				//log("DEBUG HOURS=0");
				return;
			}
			//log("processTick Bar="+BarsInProgress+";Time="+ToTime(Time[0]));	
			int iTime=ToTime(Time[0]);	
			/*if(iTime>43500&&iTime<45500){
				log("DEBUG watch="+watch+";pendingPosition="+pendingPosition+";ask="+ask+";bid="+bid+";pos="+dirOfPosition()+"tradeActive="+trade_active);
			}*/
			if(!pendingPosition){
				//first check for position
				int pos= dirOfPosition();
				
			
				
				if(pos!=0) {
						
					if(iTime>=iExitOnCloseTime&&iTime<iRestartTime){
						if(pos>0){
							//gainTotal+=net_gain;
							log(" -->>EXIT ON CLOSE LONG");
							spreadExitLongMarket();
						}else {
							log( " -->>EXIT ON CLOSE SHORT: ask="+ask+";bid="+bid);
							spreadExitShortMarket();
						}
						return;
					}
				}
				if(pos==0&&(iTime>=iLastEntryTime&&iTime<iRestartTime/*/||iTime<iStartTime*/)){
					return;
				}
				if(pos==0&&noEntry)
					return;
				if(outerTrade&&!pendingPosition													
					&&(pos>0&&ask<watch_up-0.25&&ask<currentEntry-0.5
						||pos<0&&bid>watch_down+0.25&&bid>currentEntry+0.5)){																//loss stop inside the range
					log("Detected inside loss watch_up="+watch_up+";watch_down="+watch_down+";ask="+ask+";bid="+bid+";entry="+currentEntry);
					reverse(ask,bid,pos,true);																								//inner (inside the range) reversal	
					outerTrade=false;
					//breakRange=true;																										//do it once per range - stop all future outer reversals
				}				
				if(bounceTriggered&&pos==0&&watch){																									//secondary (inner) watch detector and  pick-up (delayed trade initialization) clause
						dir=innerDir;
						
						trade_active=true;
						bounceTriggered=false;
						//target=innerTarget;
						//stop=innerStop;
						if(dir>0){
							target=(delayedTarget-ask)*4;
							stop=(bid-delayedStop)*4;
						}
						else{
							target=(bid-delayedTarget)*4;
							stop=(delayedStop-ask)*4;
						}
						if(target<1)
							trade_active=false;
						log("Pickup bounceTriggered dir="+dir+";target="+target+";stop="+stop+";breakRange="+breakRange);
				}
				if(pos!=0&&BarsInProgress==0&&CurrentBar-enteredBar>timeLimitTrade){
					log("LONG TRADE DETECTED");
					if((pos>0&&bid>currentEntry)||(pos<0&&ask<currentEntry))
						log("POSITIVE OUT");
						sell=true;
						watch=false;
				}
				if(!trade_active&&watch){																									//if broke through the range long
					if(bid>watch_up){																									//if broke up 
						if(pos==0){																											//opening a new range 
							watch=false;
							bounceTriggered=false;																									//reset secondary watch
							trade_active=true;																								//trigger the trade in the clause below
							dir=1;																											//long
							outerTrade=true;
							stop=(int)((bid-watch_down)*4+2);																				//stop two ticks outside the range
							drawRange=true;																									//trigger drawing of range lines on the chart on the next 5min bar	
							log("BREAKOUT UP Target="+target+";Stop="+stop);
							breakRange=false;	
							
							//range is reset completely as if no history
						}
						else if(pos<0){																										//if already short reverse 
							if(outerTrade){
								reverse(ask,bid,pos,false);																						// outer reversal
								outerTrade=true;	
							}
							else{
								sell=true;
								if(!preBounce){
									watch=false;
									bounceTriggered=false;
								}
								else
									preBounce=false;
								}
							
							log("SHORT OUTER REVERSAL breakRange=true;");
							breakRange=true;	
						}
					}
					else if(ask<watch_down){					
						
						//breakRange=false;
						if(pos==0){
							bounceTriggered=false;
							watch=false;
							trade_active=true;
							dir=-1;
							outerTrade=true;
							log("BREAKOUT DOWN Target="+target+";Stop="+stop);
							stop=(int)((watch_up-ask)*4+2);
							drawRange=true;
							breakRange=false;
						}
						else if(pos>0){
						    if(outerTrade){
								reverse(ask,bid,pos,false);	
								outerTrade=true;														//outer reversal
								}
							else{
								sell=true;	
								if(!preBounce){
									watch=false;
									bounceTriggered=false;
								}
								else
									preBounce=false;
							}
							log("LONG OUTER REVERSAL breakRange=true;"); 
							breakRange=true;	
						}
					}
				}
				if(!trade_active&&pos==0&&watch){
					if (allowPreBounce&&bid>watch_median&&bid<watch_up){
					
						double offset=(bid-watch_median)*4;
						/*target=offset+4;*/
						target=2+offset;
						if(ask-target/4<=watch_down)
							target=(int)(ask-watch_down)*4-2;
						target=Math.Max(target,2);
						stop=(int)((watch_up-ask)*4+3);
						
						//target=(int)((watch_median-watch_down)*4/2+offset);
						
						trade_active=true;
						dir=-1;
						preBounce=true;
						outerTrade=false;
						log("IN UPDOWN Target="+target+";Stop="+stop);
					}
					else if (allowPreBounce&&ask<watch_median&&ask>watch_down){
					
						double offset=(watch_median-ask)*4;
						//target=offset+4;
						//target=(int)((watch_up-watch_median)*4/2+offset);
						target=3+offset;
						target=Math.Max(target,2);
						if(bid+target/4>=watch_up)
							target=(int)(watch_up-bid)*4-2;
						stop=(int)((bid-watch_down)*4+3);
						trade_active=true;
						dir=1;
						outerTrade=false;;
						preBounce=true;
						log("IN DOWNUP Target="+target+";Stop="+stop);
					}
					else if(bid>watch_up||ask<watch_down){
						watch=false;
						log("idle watch ended");
					}
				
				}else {
					/*if(pos>0&&ask<watch_down||pos<0&&bid>watch_up){
						log("SELL!!!");
						sell=true;
					}*/
				}
				if(trade_active&&pos==0&&dir>0){
					sell=false;
					trailStop=false;
					log("ENTER LONG: ask="+ask+";bid="+bid);
					spreadEnterLongMarket();
				}
				else if(trade_active&&pos==0 &&(dir<0)){
					sell=false;
					trailStop=false;
					log("ENTER SHORT: ask="+ask+";bid="+bid);
					spreadEnterShortMarket();
				}
				else if(pos>0&&sell){
					log("LOSS OR TIME EXIT LONG: ask="+ask+";bid="+bid+";watch="+watch+";bounceTriggered="+
						bounceTriggered+";breakRange="+breakRange);
					spreadExitLongMarket();
					outerTrade=false;
				}
				else if(pos>0&&(bid>=currentEntry+target/4)){
					log("WIN EXIT LONG: ask="+ask+";bid="+bid+";watch="+watch+";bounceTriggered="+
						bounceTriggered+";breakRange="+breakRange);
					outerTrade=true;
					if(!breakRange&&bid<=watch_up&&bid>watch_median){
						reverse(ask,bid,pos,true);																						//side effect - sets sell
					}
					else{
						if(bid>watch_up){
						watch=false;																									//no reversal - give up on range
						bounceTriggered=false;
						}
					}
					spreadExitLongMarket();
				}
				/*
				else if(!trailStop&&pos>0&&(bid>=currentEntry+3/4)){
					log("SET TRAIL STOP: ask="+ask+";bid="+bid);
					trailStop=true;
					SetTrailStop(55);																									//seemed like a good idea at the time
					
				}*/
				else if(pos>0&&bid<=currentEntry-stop/4&&BarsInProgress==0){
					log("STOP LONG: ask="+ask+";bid="+bid);
					watch=false;																										//give up on the range;
					bounceTriggered=false;
					outerTrade=true;	
					reverse(ask,bid,pos,false);		
					spreadExitLongMarket();
																				// outer reversal
					
				}
				else if(pos<0&&sell){
					log("LOSS OR TIME EXIT SHORT: ask="+ask+";bid="+bid+";watch="+watch+";bounceTriggered="+
						bounceTriggered+";breakRange="+breakRange);
					outerTrade=false;
					spreadExitShortMarket();
				}
				else if(pos<0&&(ask<=currentEntry-target/4)){
					log("WIN EXIT SHORT: ask="+ask+";bid="+bid);
					if(!breakRange&&ask>=watch_down&&ask<watch_median){
						reverse(ask,bid,pos,true);
					}
					else{																												// keep range watch on reversals only
						if(ask<watch_down){
							watch=false;
							bounceTriggered=false;
						}
					}
					spreadExitShortMarket();
				}
				/*
				else if(!trailStop&&pos<0&&(ask<=currentEntry-3/4)){
					log("SET TRAIL STOP: ask="+ask+";bid="+bid);
					//SetTrailStop(55);
					trailStop=true;
				}*/
				else if(pos<0&&ask>=currentEntry+stop/4&&BarsInProgress==0){
					log("STOP SHORT: ask="+ask+";bid="+bid);
					watch=false;
					bounceTriggered=false;
					outerTrade=true;	
					reverse(ask,bid,pos,false);	
					spreadExitShortMarket();
				}
			}
			else {	
				if(CurrentBar>pendingBar+1&&BarsInProgress==0){																		//taking care of stuck orders
					if(pendingLongEntry||pendingShortEntry){
						if(entryOrder!=null){
							CancelOrder(entryOrder);
							log("<<< CANCELLING ENTRY ORDER >>> pendingBar="+pendingBar
								+";CurrentBar"+CurrentBar+";ask="+Closes[2][0]+";bid="+Closes[3][0]);
						}
					}
					else if(pendingLongExit||pendingShortExit){
						if(exitOrder!=null){
							CancelOrder(exitOrder);
							log(">>> CANCELLING EXIT ORDER <<< pendingBar="+pendingBar
								+";CurrentBar"+CurrentBar+";ask="+Closes[2][0]+";bid="+Closes[3][0]);
						}
					}
				}
			
			}
		}
		//<summary>
		//
		//
		//
		
		protected void reverse(double ask, double bid,int pos,bool inner){
			
			sell=true;
			outerTrade=false;			// if it is in fact outer trade, the caller will reset it back
			double tgt=enteredRange*k;//+2;;
			//double tgt=12;
			double stp=enteredRange+2;

			if(pos>0){
				innerDir=-1;
				if (inner){
					tgt=((bid-watch_median)*4+enteredRange/6);
					stp=(int)((watch_up-ask)*4+1);
				}
			}
			else{
				innerDir=1;
				if (inner){
					tgt=((watch_median-ask)*4+enteredRange/6);
					stp=(int)((bid-watch_down)*4+1);
				}
			
			}
			if(tgt>1){
				DrawDiamond("dm2"+CurrentBars[1],true,0,watch_down-0.25,Color.BlanchedAlmond);
				string tx="O-R";
				if(inner)
					tx="I-R";
					
				DrawText( "tm2"+CurrentBars[1],true,tx+" "+gainTotal.ToString("c"),0,watch_down-lineNum()*0.25,20,Color.Black, new Font("Ariel",8),StringAlignment.Near,Color.Transparent,Color.Beige, 0);
				
				drawRange=true;
				bounceTriggered=true;
				tgt=(int)tgt;
				stp=(int)stp;
				if(innerDir>0){
					delayedTarget=ask+tgt/4;
					delayedStop=bid-stp/4;
				}
				else{
					delayedTarget=bid-tgt/4;
					delayedStop=ask+stp/4;
				}
				//innerTarget=(int)tgt;
				//innerStop=(int)stp;
				log(tx+" REVERSAL ASK="+ask+";BID="+bid+";watch_up="+watch_up+";watch_down="+watch_down+";tgt="+tgt+";stp="+stp+";breakRange="+breakRange+";delayedTarget="+delayedTarget+";delayedStop="+delayedStop);
			}
			else {
				log("TARGET("+tgt+") TOO SMALL TO REVERSE target="+target);
				bounceTriggered=false;
				watch=false;
				//breakRange=true;
			}
		}
		/*protected override void OnMarketData(MarketDataEventArgs e){
		//extra chance for on close exit - on bid and ask updates	
			if(!active)
				return;
			if(e.MarketDataType == MarketDataType.Ask||e.MarketDataType == MarketDataType.Bid){	
				processTick(GetCurrentAsk(1),GetCurrentBid(1));
			}
			
		}*/
		protected override void OnOrderUpdate(IOrder order){
    	if (entryOrder != null && entryOrder == order){
         //Print(order.ToString());
			if (order.OrderState == OrderState.Cancelled){
				log("---->>>> Entry Order Cancelled");
				pendingLongEntry=false;
				pendingShortEntry=false;
				pendingPosition=false;
				entryOrder = null;
			 }
    	}
		else if (exitOrder != null && exitOrder == order){
         //Print(order.ToString());
			if (order.OrderState == OrderState.Cancelled){
				log("---->>>> Exit Order Cancelled");
				pendingLongExit=false;
				pendingShortExit=false;
				pendingPosition=false;
				exitOrder = null;
			 }
    	}
}
		protected override void OnPositionUpdate(IPosition position){
			if(pendingPosition){
				if (Positions[1].MarketPosition == MarketPosition.Flat){
					if(pendingLongExit||pendingShortExit){
						log("EXITED FLAT: watch="+watch+";bounceTriggered="+
							bounceTriggered+";breakRange="+breakRange);
						pendingPosition=false;
						pendingLongExit=false;
						pendingShortExit=false;
						currentEntry=0;
					}
				}
				if(pendingLongEntry){
					if (Positions[1].MarketPosition == MarketPosition.Long){
						log("ENTERED LONG: watch="+watch+";bounceTriggered="+
							bounceTriggered+";breakRange="+breakRange);
						pendingPosition=false;
						pendingLongEntry=false;
						enteredBar=CurrentBars[0];
						enteredHeight=Highs[0][0]+1;
					}
				}
				else if(pendingShortEntry){
					if (Positions[1].MarketPosition == MarketPosition.Short){
						log("ENTERED SHORT: watch="+watch+";bounceTriggered="+
						bounceTriggered+";breakRange="+breakRange);
						pendingPosition=false;
						pendingShortEntry=false;
						//DrawArrowUp(""+CurrentBar,true,0,Highs[0][0]+1,Color.Yellow);
						enteredBar=CurrentBars[0];
						enteredHeight=Highs[0][0]+1;
					}
				}
			}
		}
		protected override void OnExecution(IExecution execution){
			// Remember to check the underlying IOrder object for null before trying to access its properties
			if (execution.Order != null && execution.Order.OrderState == OrderState.Filled){
				//log("Execution:"+execution.ToString());
				if (entryOrder != null && entryOrder == execution.Order){
					currentEntry=execution.Order.AvgFillPrice;
					log("ENTERED at "+currentEntry);
					enteredBar=CurrentBars[0];
				}
				else{
					if(execution.Order.OrderAction.CompareTo(OrderAction.Sell)==0){
						pendingLongExit=true;
						pendingShortExit=false;
					}
					else{
						pendingShortExit=true;
						pendingLongExit=false;
					}
					double currentExit=execution.Order.AvgFillPrice;
					double gain=0;
					
					if(pendingLongExit){
						gain=(currentExit-currentEntry);
					}else if(pendingShortExit){
						gain=(currentEntry-currentExit);
					}
					double net_gain=gain*4*12.5-4;
					
					gainTotal+=net_gain;
					log("EXITED at "+currentExit+";$$$$$$ gain="+gain+";net="+net_gain.ToString("C")+"; $$$$$ total="+gainTotal.ToString("C"));
				
					if(exitOrder==null){
						log("RESET WATCH");
						watch=false;
						bounceTriggered=false;
					}
					
					exitOrder=null;		
				}	
				
			}
			
		}
		protected override void OnTermination(){
  			string s="Finished Simulated Session "+strategyName+"  "+ToDay(firstTime)+"-"+ToDay(t)+"\n 	Range="+Range+";RangePeriod="+RangePeriod+";MinRangePeriod="+minRangePeriod+"\n GainTotal="+gainTotal.ToString("c");
			SendMail("adaptive.kolebator@gmail.com", "adaptive.kolebator@gmail.com", "Backtest Tesults", s);
			log("SENT MAIL");
		}	
		protected void spreadEnterLong(){

			pendingPosition=true;
			pendingLongEntry=true;
			pendingBar=CurrentBar;
			orderID=name+CurrentBar;
			entryOrder=EnterLongLimit(1,true,NumContractrs,Closes[2][0],orderID);
		}
		protected void spreadEnterShort(){
			pendingPosition=true;
			pendingShortEntry=true;
			pendingBar=CurrentBar;
			orderID=name+CurrentBar;
			entryOrder=EnterShortLimit(1,true,NumContractrs,Closes[3][0],orderID);
		}
		protected void spreadEnterLongMarket(){

			pendingPosition=true;
			pendingLongEntry=true;
			pendingBar=CurrentBar;
			orderID=name+CurrentBar;
			entryOrder=EnterLong(1,NumContractrs,orderID);
		}
		protected void spreadEnterShortMarket(){
			pendingPosition=true;
			pendingShortEntry=true;
			pendingBar=CurrentBar;
			orderID=name+CurrentBar;
			entryOrder=EnterShort(1,NumContractrs,orderID);
		}
		protected void spreadExitLong(){
			pendingPosition=true;
			pendingLongExit=true;
			pendingBar=CurrentBar;
			exitOrder=ExitLongLimit(1,true,NumContractrs,Closes[3][0],orderID,orderID);
		}
		protected void spreadExitShort(){
			pendingPosition=true;
			pendingShortExit=true;
			pendingBar=CurrentBar;
			exitOrder=ExitShortLimit(1,true,NumContractrs,Closes[2][0],orderID,orderID);
		}
		protected void spreadExitLongMarket(){
			pendingPosition=true;
			pendingLongExit=true;
			pendingBar=CurrentBar;
			exitOrder=ExitLong(1,NumContractrs,orderID,orderID);			
		}
		protected void spreadExitShortMarket(){
			pendingPosition=true;
			pendingShortExit=true;
			pendingBar=CurrentBar;
			exitOrder=ExitShort(1,NumContractrs,orderID,orderID);			
		}
		protected int dirOfPosition(){
			Position pES=Positions[1];
			
			if(pES.MarketPosition==MarketPosition.Long)
				return 1;
			if(pES.MarketPosition==MarketPosition.Short)
				return -1;
			return 0;
		}
			protected void initLog(){
			
			log("**********************    "+name+"   ************************************");
			log("Range="+Range+";RangePeriod="+RangePeriod+";profitTarget="+profitTarget+";stopLoss="+stopLoss);
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
        public int RangePeriod
        {
            get { return rangePeriod; }
            set { rangePeriod = value; }
        } 
		[Description("Maximum Range")]
        [GridCategory("Parameters")]
        public double Range
        {
            get { return range; }
            set { range = value; }
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
            get { return minRangePeriod; }
            set { minRangePeriod = value; }
        } 
		[Description("")]
        [GridCategory("Parameters")]
        public double MinRange
        {
            get { return minRange; }
            set { minRange= value; }
        } 
		[Description("Target range multiplier: target=range*K")]
        [GridCategory("Parameters")]
        public double K
        {
            get { return k; }
            set {  k= value; }
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
        public bool AllowPreBounce
        {
            get { return allowPreBounce; }
            set {  allowPreBounce= value; }
        } 
        #endregion
    }
}
