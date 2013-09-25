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
    [Description("Dumb Ranger")]
    public class Walker : Strategy
    {
        #region Variables
        //properties
		private int numContractrs = 1; // Default setting for NumContractrs
		private bool tradingLive=false; //Default setting for TradingLive
		private string contractMonth="12-13"; //Default setting for ContractMonth
		private string strategyName="Walker";  //Default setting for Name
		private int range=10;
		private int rangePeriod=18;
		private int minRangePeriod=4;
		private double profitTarget=150;
		private double stopLoss=175;
		
		private double stop = 0; 
		private double target = 0;
		int dir=0;
		int c=0;
		private string name=""; 
		double gainTotal=0;
		private bool pendingPosition=false;
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
		
		private DateTime t;
		private bool v=false;
		private double sma;
		private double adj=0.0;
		bool sell=false;
		TimeSpan tw;
		bool trade_active=false;
		int total=0;
		bool vv=true;
		bool watch=false;
		double watch_up=0;
		double watch_down=0;
		string orderID;
		int rangeStartBar=0;
		int enteredBar=0;
		double enteredHeight=0.0;
		int enteredRange=0;
		int enteredPeriod=0;
		bool closedAbove=false;
		
		private IOrder entryOrder = null;
		private IOrder exitOrder = null;
		int pendingBar=0;		
		int currentBar=0;
		bool drawRange=false;
		
		bool innerWatch=false;
		int innerDir=0;
		int innerTarget;
		int innerStop;
		DateTime firstTime;
		bool trailStop=false;
		
		
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
			Add(firstInstrumentSpec,PeriodType.Minute,5,MarketDataType.Ask); // 2 included in live for accurate timestamps
			Add(firstInstrumentSpec,PeriodType.Minute,5,MarketDataType.Bid); //3
			Add(firstInstrumentSpec,PeriodType.Tick,1,MarketDataType.Ask); // 4 included in live for accurate timestamps
			Add(firstInstrumentSpec,PeriodType.Tick,1,MarketDataType.Bid); // 5 included in live for accurate timestamps
		
			name=strategyName;
		
			SetStopLoss(stopLoss);
			//SetTrailStop(stopLoss);
			//SetProfitTarget(profitTarget);
		
		}
	
		/// <summary>
		/// Called on each bar update event (incoming tick)
		/// </summary>
		protected override void OnBarUpdate(){
		//	if(BarsInProgress==2 ||BarsInProgress==3||BarsInProgress==4||BarsInProgress==5)
		//		return;
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
			
			
			
			if(BarsInProgress==0){												// only on 5 minute bars
				int iTime=ToTime(Time[0]);	
				//if(iTime>=iLastEntryTime||iTime<iStartTime){
				if((iTime>=iLastEntryTime&&iTime<iRestartTime/*/||iTime<iStartTime*/)){
					watch=false;
					return;
				}
				if(CurrentBar==currentBar)
					return;
				currentBar=CurrentBar;
				if(drawRange){
					//log("DRAW LINE rangeStartBar="+rangeStartBar+";CurrentBar="+CurrentBars[0]);
					DrawLine("uwline"+CurrentBars[1],true,CurrentBars[0]-rangeStartBar+enteredPeriod,watch_up,1,watch_up,Color.DarkOliveGreen,DashStyle.Solid,1);
					DrawLine("dwline"+CurrentBars[1],true,CurrentBars[0]-rangeStartBar+enteredPeriod,watch_down,1,watch_down,Color.DarkBlue,DashStyle.Solid,1);
					DrawText( "t"+CurrentBars[1],true,"R:"+enteredRange+"  Tgt:"+target+ "  Stop:"+stop+"  Prd:"+enteredPeriod,CurrentBars[0]-rangeStartBar+enteredPeriod,watch_up+0.50,20,Color.Black, new Font("Ariel",8),StringAlignment.Near,Color.Transparent,Color.Beige, 0);
					drawRange=false;	
						
				}
				int pos= dirOfPosition();
				//if(!pendingPosition&&(pos>0&&Closes[3][0]>currentEntry+(enteredRange/4)/3||pos<0&&Closes[2][0]<currentEntry-(enteredRange/4)/3))
				//	SetTrailStop(35);
			//		sell=true;
				// detect first close above the entry and thn below
			/*	if(watch_up!=0&&!closedAbove&&(pos>0&&Closes[5][0]>=currentEntry||pos<0&&Closes[4][0]<=currentEntry)){
					closedAbove=true;
					log("CLOSED ABOVE ask="+Closes[2][0]+";bid="+Closes[3][0]);
					DrawDot("dm2"+CurrentBars[1],true,0,watch_down-0.25,Color.BlanchedAlmond);
					DrawText( "tm2"+CurrentBars[1],true,"CLOSED ABOVE ",0,watch_down-0.50,20,Color.Black, new Font("Ariel",8),StringAlignment.Near,Color.Transparent,Color.Beige, 0);
					
				}*/
			//	if(!pendingPosition&&(pos>0&&Closes[3][0]<currentEntry-(enteredRange/4)/2||pos<0&&Closes[2][0]>currentEntry+(enteredRange/4)/2))
			//		sell=true;
				
				if(closedAbove&&!pendingPosition&&(pos>0&&Closes[3][0]<currentEntry-0.25||pos<0&&Closes[2][0]>currentEntry+0.25)){
					sell=true;
					log(" SELL TRIGGER ON CLOSED ABOVE REVERSAL");
					closedAbove=false;
					if(pos>0){
						double mark=((enteredRange/4)*3)/4;
						if(Closes[3][0]<watch_up-mark){
							innerWatch=true;
							innerDir=-1;
							innerTarget=(int)(Math.Max(((watch_up-Closes[2][0])/2)*4,2));
							innerStop=(int)((Closes[2][0]-watch_down)*4+4);
							log("000000  SET INNER WATCH DOWN");
							DrawDiamond("dm2"+CurrentBars[1],true,0,watch_up+0.25,Color.Red);
							DrawText( "tm2"+CurrentBars[1],true,"CLOSED ABOVE REVERSAL",0,watch_up+0.50,20,Color.Black, new Font("Ariel",8),StringAlignment.Near,Color.Transparent,Color.Beige, 0);
					
						}
					}
					else {
						if(pos<0){
							double mark=((enteredRange/4)*3)/4;
							if(Closes[2][0]>watch_down+mark){
								innerWatch=true;
								innerDir=1;
								innerTarget=(int)(Math.Max(((Closes[3][0]-watch_down)/2)*4,2));
								innerStop=(int)((watch_up-Closes[3][0])*4+4);
								log("000000  SET INNER WATCH UP");
								DrawDiamond("dm2"+CurrentBars[1],true,0,watch_down-0.75,Color.Red);
								DrawText( "tm2"+CurrentBars[1],true,"CLOSED BELOW REVERSAL",0,watch_down-1,20,Color.Black, new Font("Ariel",8),StringAlignment.Near,Color.Transparent,Color.Beige, 0);
							}
						}
					}
				}
				
				if(CurrentBars[0]>rangePeriod&&pos==0&&!pendingPosition&&!watch){
					//range detection
					closedAbove=false;
					trade_active=false;
					int r=rangePeriod+1;
					while(r>minRangePeriod){
						r--;
						double mx=Math.Max(MAX(Closes[3],r)[0],MAX(Opens[3],r)[0]);				// high-end of the body (narrow) range
						double mn=Math.Min(MIN(Closes[2],r)[0],MIN(Opens[2],r)[0]); 			// low-end of the body range
						double mxh=MAX(Highs[3],r)[0];            								// high end of the full (wide) range 
						double mnh=MIN(Lows[2],r)[0];											// low-end of the wide range
						
						double wr=mxh-mnh;														// wide range, high to lows
						double nr=mx-mn;										    			// narrow range, for bodies only	
						if(nr<=range/4&&nr>2/4){								   				// max range					
										
							double rm=mnh+wr/2;													// range median
							
							//accumullators
							double sbup=0, sbdn=0, sbh=0, sbl=0,sbm=0;
							
							//is first half a range on its own	
							int hrp=r/2;
							int count=0;
							bool touchedTop=false;												// at least one bar in this half touched top
							bool touchedBottom=false;											// at least one bar in this half touched bottom
							
							for(int i=0;i<hrp;i++){
								double top=Math.Max(Opens[3][i],Closes[3][i]);
								double btm=Math.Min(Opens[2][i],Closes[2][i]);
								sbup+=top;       												// high-end of the candle body
								sbdn+=btm;       												// low-end of the candle body
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
									sbup+=top;      		 									// high-end of the candle body
									sbdn+=btm;       											// low-end of the candle body
									if(top>=mx&&btm<=rm)
										touchedTop=true;
									if(btm<=mn&&top>=rm)
										touchedBottom=true;
									//log("TRACE sbdn["+i+"]+"+Math.Min(Opens[3][i],Closes[3][i]));
									//sbh+=Highs[3][i];											// high
									//sbl+=Lows[2][i];											// low
									count++;
								}
								//averages:
								abup=sbup/count;
								abdn=sbdn/count;
								if((abdn<rm&&abup>rm)/*&&touchedTop&&touchedBottom*/){			// range detected
									enteredRange=(int)(mxh-mnh)*4;
									//if(enteredRange>4){
									//verify that the data is good:
									int lastBid=ToTime(Times[5][0]);
									int lastAsk=ToTime(Times[4][0]);
									if((ToTime(Time[0])-lastBid<60)&&ToTime(Time[0])-lastAsk<60){					//gap less than 60 secs
										watch=true;
										watch_up=mxh;
										watch_down=mnh;
										//target=Math.Max(4*(wr-nr)*2,2);
										 target=Math.Max((int)(enteredRange/4)/4,2);	// right?
									   // target=Math.Max(4*(wr-nr),2);
										//target=Math.Max(1,12);
										//target=Math.Max((int)((enteredRange+enteredPeriod)/3),2);	
										rangeStartBar=CurrentBars[0];
										enteredPeriod=r;
										log("$$$ START WATCH target="+target+";range="+enteredRange+";rangePeriod="+enteredPeriod+";rm="+rm+";abdn="+abdn+";abup="+abup);
										DrawDiamond("dm"+CurrentBars[1],true,0,watch_up+0.25,Color.Blue);
										if(gainTotal>0)
											DrawText( "tm2"+CurrentBars[1],true,"TOTAL: "+gainTotal.ToString("c") ,0,watch_up+0.25,20,Color.Green, new Font("Ariel",8),StringAlignment.Near,Color.Transparent,Color.Beige, 0);
										else
											DrawText( "tm2"+CurrentBars[1],true,"TOTAL: "+gainTotal.ToString("c") ,0,watch_up+0.25,20,Color.Red, new Font("Ariel",8),StringAlignment.Near,Color.Transparent,Color.Beige, 0);
										break;
									}
									
									
								}
							}
						}
					}
				}
			
				if(watch&&!trade_active)
					log("$$$ WATCH UP="+watch_up+";DOWN="+watch_down);
				if(pos>0){
					if(Closes[5][0]>currentEntry&&Closes[5][0]>Closes[5][1]){
						adj+=0.25;
						log("ADJ++:"+adj);
					}
					else if(Closes[5][0]<Closes[5][1]&&adj>0){
						adj=0;
						log("ADJ=0");
						
					}
				}
				if(pos<0){
					if(Closes[4][0]<currentEntry&& Closes[4][0]<Closes[4][1]){
						adj+=0.25;
						log("ADJ++:"+adj);
					}
					else if(Closes[4][0]>Closes[4][1]&&adj>0){
						adj=0;
						log("ADJ=0");
					}
				}
			}
			if(BarsInProgress==4||BarsInProgress==5){
				if(Historical&&!tradingLive){ // when backtesting to the trades
					processTick(Closes[4][0],Closes[5][0]);
				}
				else if(!Historical&&tradingLive){
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
			if(!pendingPosition){
				//first check for position
				int pos= dirOfPosition();
				
				int iTime=ToTime(Time[0]);	
				
				if(pos!=0) {
						
					if(iTime>=iExitOnCloseTime&&iTime<iRestartTime){
						if(pos>0){
							PrintWithTimeStamp(name+" -->>EXIT ON CLOSE LONG: ask="+ask+";bid="+bid);
							double gain=(bid-currentEntry);
							double net_gain=gain*4*12.5-4;
							gainTotal+=net_gain;
							log(" -->>EXIT ON CLOSE LONG: ask="+ask+";bid="+bid+";gain="+gain+";net="+net_gain.ToString("C")+";total="+gainTotal.ToString("C"));
							spreadExitLongMarket();
						}else {
							double gain=(currentEntry-ask);
							double net_gain=gain*4*12.5-4;
							gainTotal+=net_gain;
							PrintWithTimeStamp(name+ " -->>EXIT ON CLOSE SHORT: ask="+ask+";bid="+bid+";gain="+gain+";net="+net_gain.ToString("C")+";total="+gainTotal.ToString("C"));
							log( " -->>EXIT ON CLOSE SHORT: sask="+ask+";bid="+bid);
							spreadExitShortMarket();
						}
						return;
					}
				}
				if(pos==0&&(iTime>=iLastEntryTime&&iTime<iRestartTime/*/||iTime<iStartTime*/)){
					//watch=0;99
					return;
				}
			
				if(watch_up!=0&&!innerWatch/*&&!closedAbove*/&&!pendingPosition&&(pos>0&&ask<watch_up-0.50&&ask<currentEntry+0.25||pos<0&&bid>watch_down+0.50&&bid>currentEntry+0.25)){
					sell=true;
					log(" SELL TRIGGER ON NO CLOSE REVERSAL ASK="+ask+";BID="+bid+";watch_up="+watch_up+";watch_down="+watch_down);
					DrawDiamond("dm2"+CurrentBars[1],true,0,watch_down-0.25,Color.BlanchedAlmond);
					DrawText( "tm2"+CurrentBars[1],true,"NAKED REVERSAL",0,watch_down-0.50,20,Color.Black, new Font("Ariel",8),StringAlignment.Near,Color.Transparent,Color.Beige, 0);
					innerWatch=true;
					watch=false;
					watch_up=0;
					//watch_down=0;
					if(pos>0){
						innerDir=-1;
					}
					else{
						innerDir=1;
					}
					innerTarget=6;
					innerStop=12;
				}				
				if(innerWatch&&pos==0){
						dir=innerDir;
						trade_active=true;
						innerWatch=false;
						target=innerTarget;
						stop=innerStop;
						log("Pickup innerWatch dir="+dir+";target="+target+";stop="+stop);
				}
				if(!trade_active&&watch&&pos==0){
					if(bid>watch_up/*+breakSpread/4*/){
						trade_active=true;
						watch=false;
						//watch_up=0;
						dir=1;
						//DrawDiamond("dm"+CurrentBar,true,1,Highs[0][0]+1,Color.Azure);
						stop=(int)((bid-watch_down)*4+2);
						drawRange=true;
						log("BREAKOUT UP Target="+target+";Stop="+stop);
						
					}
					else if(ask<watch_down/*-breakSpread/4*/){
						trade_active=true;
						watch=false;
						//watch_down=0;
						dir=-1;
						log("BREAKOUT DOWN Target="+target+";Stop="+stop);
						
						//DrawDiamond("dm"+CurrentBar,true,1,Highs[0][0]+1,Color.Bisque);
						stop=(int)((watch_up-ask)*4+2);
						drawRange=true;
					}
				}
				
				if(trade_active&&pos==0&&dir>0/*&&BarsInProgress==0*/){
					adj=0;
					sell=false;
					trailStop=false;
					PrintWithTimeStamp(name+" ENTER LONG: ask="+ask+";bid="+bid);
					log(" ENTER LONG: ask="+ask+";bid="+bid);
					//currentEntry=ask;
				
					spreadEnterLongMarket();
				}
				else if(trade_active&&pos==0 &&(dir<0/*&&BarsInProgress==0*/)){
					adj=0;
					sell=false;
					trailStop=false;
					PrintWithTimeStamp(name+" ENTER SHORT: ask="+ask+";bid="+bid);
					log(" ENTER SHORT: ask="+ask+";bid="+bid);
					//currentEntry=bid;
					
					spreadEnterShortMarket();
				}
				else if(pos>0&&(bid>=currentEntry+target/4+adj||sell)){
					log(" EXIT LONG: ask="+ask+";bid="+bid);
					spreadExitLongMarket();
				}
				else if(!trailStop&&pos>0&&(bid>=currentEntry+3/4)){
					log(" SET TRAIL STOP: ask="+ask+";bid="+bid);
					trailStop=true;
					SetTrailStop(55);
				}
				else if(pos>0&&bid<=currentEntry-stop/4-adj&&BarsInProgress==0){
					log(" STOP LONG: ask="+ask+";bid="+bid);
					spreadExitLongMarket();
				}
				else if(pos<0&&(ask<=currentEntry-target/4+adj*2||sell)/*&&BarsInProgress==0*/){
					log(" EXIT SHORT: ask="+ask+";bid="+bid);
					spreadExitShortMarket();
				}
				else if(!trailStop&&pos<0&&(ask<=currentEntry-3/4)){
					log(" SET TRAIL STOP: ask="+ask+";bid="+bid);
					SetTrailStop(55);
					trailStop=true;
				}
				else if(pos<0&&ask>=currentEntry+stop/4-adj*2&&BarsInProgress==0){
					PrintWithTimeStamp(name+" STOP SHORT: ask="+ask+";bid="+bid);
					/*double gain=(currentEntry-ask);
					double net_gain=gain*4*12.5-4;
					gainTotal+=net_gain;*/
					log(" STOP SHORT: ask="+ask+";bid="+bid);
					/*top=ask+Target;
					bottom=bid-1;
					PrintWithTimeStamp(name+" SET RANGE TOP:"+top+", BOTTOM:"+bottom);
					log(" SET RANGE TOP:"+top+", BOTTOM:"+bottom);*/
					spreadExitShortMarket();
				}
			}
			else {
				if(CurrentBar>pendingBar+1&&BarsInProgress==0){
					if(pendingLongEntry||pendingShortEntry){
						if(entryOrder!=null){
							CancelOrder(entryOrder);
							log("<<< CANCELLING ENTRY ORDER >>> pendingBar="+pendingBar+";CurrentBar"+CurrentBar+";ask="+Closes[2][0]+";bid="+Closes[3][0]);
						}
					}
					else if(pendingLongExit||pendingShortExit){
						if(exitOrder!=null){
							CancelOrder(exitOrder);
							log(">>> CANCELLING EXIT ORDER <<< pendingBar="+pendingBar+";CurrentBar"+CurrentBar+";ask="+Closes[2][0]+";bid="+Closes[3][0]);
						}
					}
				}
			
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
						PrintWithTimeStamp(name+" EXITED FLAT");
						log(" EXITED FLAT");
						pendingPosition=false;
						pendingLongExit=false;
						pendingShortExit=false;
						currentEntry=0;
					}
				}
				if(pendingLongEntry){
					if (Positions[1].MarketPosition == MarketPosition.Long){
						PrintWithTimeStamp(name+" ENTERED LONG");
						log(" ENTERED LONG");
						pendingPosition=false;
						pendingLongEntry=false;
						//DrawArrowUp(""+CurrentBar,true,0,Highs[0][0]+1,Color.Blue);
						enteredBar=CurrentBars[0];
						enteredHeight=Highs[0][0]+1;
					}
				}
				else if(pendingShortEntry){
					if (Positions[1].MarketPosition == MarketPosition.Short){
						PrintWithTimeStamp(name+" ENTERED SHORT");
						log(" ENTERED SHORT");
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
				else
				/*if (exitOrder != null && exitOrder == execution.Order)*/{
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
					Color c;
					if(gain>0)
						c=Color.Green;
					else
						c=Color.Red;
					//DrawArrowDown(name+CurrentBar,true,0,Highs[0][0]+1,c);
					//log("Draw CurrentBar="+CurrentBars[0]+";enteredBar="+enteredBar);
					//DrawLine("line"+CurrentBar,true,CurrentBars[0]-enteredBar,enteredHeight,0,Highs[0][0]+1,c,DashStyle.Dot,2);
				
					
						
				}	
				//Print(execution.ToString());
				
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
        public int Range
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
        } 
		[Description("")]
        [GridCategory("Parameters")]
        public double StopLoss
        {
            get { return stopLoss; }
            set { stopLoss = value; }
        } */
		[Description("")]
        [GridCategory("Parameters")]
        public int MinRangePeriod
        {
            get { return minRangePeriod; }
            set { minRangePeriod = value; }
        } 
		
        #endregion
    }
}
