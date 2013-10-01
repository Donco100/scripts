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
    public class TexasRanger : Base
    {
		#region Types
		
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
		public struct Candle{
			public double high;
			public double low;
			public int height;
			public int body;
			public int hair;
			public int beard;
			public int dir;
			public double bottom;
			public double top;
			public double topQuarter;
			public double bottomQuarter;
			public double topBodyQuarter;
			public double bottomBodyQuarter;
		}
		public enum RANGE_EVENT {DETECT,BREAKOUT,BREAKOUT_CLOSE,END};	//END is for unknown reason stop on loss happened
		public enum TRADE_EVENT {EXIT_ON_EOD, SWING_IN_SHORT,SWING_IN_LONG, SWING_OUT_LONG,SWING_OUT_SHORT,EXIT_LONG_LONG_TRADE,EXIT_LONG_SHORT_TRADE,EXIT_LONG,EXIT_SHORT,STOP_LONG,STOP_SHORT,KICKASS_LONG,KICKASS_SHORT};
		public enum TRADE_TYPE {SWING_IN, SWING_OUT,KICKASS};
		#endregion
		
        #region Variables
        //properties
		
		private double 	maxRange=10;
		private double 	minRange=4;
		private int 	maxPeriod=18;
		private int 	minPeriod=3;
		private int 	timeLimitTrade=12;
		private int     tm=1;	
		private bool    allowSwingOut=true;		
		private bool 	allowPreBounce=false;
		private bool	allowLongTradesKill=true;
		private bool    allowKickass=false;
		bool 		virgin		=	true;																			//detects first live tick to reset any existing ranges
		Range		range		=	new Range();
		Candle[]    candles		=	new Candle[3];
		#endregion			
		protected override void start(){}		
		protected  override void barDetector(){	
			if(!Historical){
				if(virgin){
					virgin=false;
					range.active=false;
					log("RESET VIRGIN");
				}
			}
			logState("barDetector");
			int iTime=ToTime(Time[0]);	
			
			if((iTime>=iLastEntryTime&&iTime<iRestartTime)){													// detect regular trading pause TODO: get trading hours from the exchange to support short days
				range.active=false;
				return;
			}
			
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
				log("R");
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
				loadCandlesticks();
				if(shpalaDownUp())
					processTradeEvent(TRADE_EVENT.KICKASS_LONG);
				else if(shpalaUpDown())
					processTradeEvent(TRADE_EVENT.KICKASS_SHORT);
			}
		}
		//This method measures and deconstructs last three candles to be used by kickass indicators
		protected void loadCandlesticks(){
			for(int i=0;i<3;i++){
				candles[i]=new Candle();
				candles[i].high=Highs[3][i];																		//based on asks
				candles[i].low=Lows[2][i];																			//based on bids	
				candles[i].height=(int)((candles[i].high-candles[i].low)*tf);
				candles[i].top=Math.Max(Opens[3][i],Closes[3][i]);
				candles[i].bottom=Math.Min(Opens[2][i],Closes[2][i]);
				candles[i].body=(int)((candles[i].top-candles[i].bottom)*tf);
				candles[i].hair=(int)((candles[i].high=candles[i].top)*tf);
				candles[i].beard=(int)((candles[i].bottom-candles[i].low)*tf);
				if(Opens[2][i]>Closes[2][i])
					candles[i].dir=-1;
				else	
					candles[i].dir=-1;
				double quarter=(candles[i].height/4)/tf;
				double bodyQuarter=(candles[i].body/4)/tf;
				candles[i].topQuarter=candles[i].high-quarter;
				candles[i].bottomQuarter=candles[i].low+quarter;
				candles[i].topBodyQuarter=candles[i].top-bodyQuarter;
				candles[i].bottomBodyQuarter=candles[i].bottom+bodyQuarter;
			}
		}
		protected string dumpCandlesticks(){
			string output="";
			for(int i=0;i<3;i++){
				output+="CANDLE["+i+"]: high="+candles[i].high+";low="+candles[i].low+";height="+candles[i].height+";top="+candles[i].top+
					";bottom="+candles[i].bottom+";body="+candles[i].body+";hair="+candles[i].hair+";beard="+candles[i].beard+";dir="+candles[i].dir+
					";topQuarter="+candles[i].topQuarter+";bottomQuarter="+candles[i].bottomQuarter+";topBodyQuarter="+candles[i].topBodyQuarter+
					";bottomBodyQuarter="+candles[i].bottomBodyQuarter+"\n";
			}
			return output;
		}
	
		protected string dumpConditions(Dictionary<string, bool> conds ){
			string output="CONDS:";
			foreach (KeyValuePair<string, bool> pair in conds){
				output+=pair.Key+","+pair.Value+";";
			}
			return output;
		}
		protected bool evalConditions(String pattern,Dictionary<string, bool> conds ){
			string output="CONDS:";
			foreach (KeyValuePair<string, bool> pair in conds){
				if(!pair.Value){
					log(pattern+" COND FAILED:"+pair.Key);
					return false;
				}
			}
			return true;
		}
		protected bool shpalyDownUp(){
			const int PRE_PERIOD=6;																					//sticks
			const int FIRST_STICK_HEIGHT=7;																			//ticks
			const int SECOND_STICK_HEIGHT=4;																		//ticks
			const int FIRST_HAIRCUT=1;																				//ticks
			const int SECOND_HAIRCUT=2;																				//ticks
			
			if(TradingLive){
				logState("HEARTBEAT: prevCandleDir="+prevCandleDir+";candleDir="+candleDir+";prevCandleHeight="+prevCandleHeight+";down_tip="+down_tip+";up_tip="+up_tip+";Close[0]="+Close[0]+";Math.Min(MIN(Opens[2],3)[2],MIN(Closes[2],3)[2])="+Math.Min(MIN(Opens[2],3)[2],MIN(Closes[2],3)[2]));
			}
			//log("DEBUG prevCandleDir="+prevCandleDir+";candleDir="+candleDir+";candleHeight="+candleHeight+";prevCandleBody="+prevCandleBody+";prevCandleTop="+prevCandleTop+";prevCandleBottom="+prevCandleBottom+";body_up_tip="+body_up_tip+";body_down_tip="+body_down_tip+";prevCandleHeight="+prevCandleHeight+";down_tip="+down_tip+";up_tip="+up_tip+";Close[0]="+Close[0]+";Math.Min(MIN(Opens[2],3)[2],MIN(Closes[2],3)[2])="+Math.Min(MIN(Opens[2],3)[2],MIN(Closes[2],3)[2]));
			Dictionary<string, bool> conds =new Dictionary<string, bool>();
			conds.Add("FirstRed",candles[1].dir==-1);
			conds.Add("SecondGreen",candles[0].dir==1);
			conds.Add("FirstHeight",candles[1].height>=FIRST_STICK_HEIGHT);
			conds.Add("SecondHeight",candles[0].height>=SECOND_STICK_HEIGHT);
			conds.Add("StickingDown",candles[1].topBodyQuarter<=Math.Min(MIN(Opens[2],PRE_PERIOD)[2],MIN(Closes[2],PRE_PERIOD)[2]);
			conds.Add("SecondCloseGTEQFirstBodyTop",candles[0].top>=candles[1].topBodyQuarter);
			conds.Add("FirstHaircut",candles[1].hair<=FIRST_HAIRCUT);
			conds.Add("SecondHaircut",candles[0].hair<=SECOND_HAIRCUT);
			logState(dumpCandlesticks+"\n"+dumpConditions(conds));
			if(evalConditions("ShpalyDownUp",conds)){
				if(candles[1].height>=12){
					trade.target=12;
					trade.stop=12;
				}
				else {
					trade.target=4;
					trade.stop=12;
				}
				trade.signal="kickass shpaly downup";
				return true;
				
			}
			return false;
		}
		protected bool shpalyUpDown(){
			const int PRE_PERIOD=6;																					//sticks
			const int FIRST_STICK_HEIGHT=7;																			//ticks
			const int SECOND_STICK_HEIGHT=4;																		//ticks
			const int FIRST_BEARD=1;																				//ticks
			const int SECOND_BEARD=2;																				//ticks
			
			//log("DEBUG prevCandleDir="+prevCandleDir+";candleDir="+candleDir+";candleHeight="+candleHeight+";prevCandleBody="+prevCandleBody+";prevCandleTop="+prevCandleTop+";prevCandleBottom="+prevCandleBottom+";body_up_tip="+body_up_tip+";body_down_tip="+body_down_tip+";prevCandleHeight="+prevCandleHeight+";down_tip="+down_tip+";up_tip="+up_tip+";Close[0]="+Close[0]+";Math.Min(MIN(Opens[2],3)[2],MIN(Closes[2],3)[2])="+Math.Min(MIN(Opens[2],3)[2],MIN(Closes[2],3)[2]));
			Dictionary<string, bool> conds =new Dictionary<string, bool>();
			conds.Add("FirstGreen",candles[1].dir==1);
			conds.Add("SecondRed",candles[0].dir==-1);
			conds.Add("FirstHeight",candles[1].height>=FIRST_STICK_HEIGHT);
			conds.Add("SecondHeight",candles[0].height>=SECOND_STICK_HEIGHT);
			conds.Add("StickingUp",candles[1].bottomBodyQuarter>=Math.Max(MAX(Opens[3],PRE_PERIOD)[2],MAX(Closes[3],PRE_PERIOD)[2]);
			conds.Add("SecondCloseLTEQFirstBodyBottom",candles[0].bottom<=candles[1].bottomBodyQuarter);
			conds.Add("FirstBeard",candles[1].beard<=FIRST_BEARD);
			conds.Add("SecondBeard",candles[0].beard<=SECOND_BEARD);
			logState(dumpCandlesticks+"\n"+dumpConditions(conds));
			if(evalConditions("ShpalyUpDown",conds)){
				if(candles[1].height>=12){
					trade.target=12;
					trade.stop=12;
				}
				else {
					trade.target=4;
					trade.stop=12;
				}
				trade.signal="kickass shpaly dupdown";
				return true;
			}
			return false;
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
				case TRADE_EVENT.KICKASS_LONG:
					trade.type="KICKASS";
					enterLongMarket();
					logState("TRADE EVENT "+e);	
					break;
				case TRADE_EVENT.KICKASS_SHORT:
					trade.type="KICKASS";
					enterShortarket();
					logState("TRADE EVENT "+e);	
					break;	
				case TRADE_EVENT.EXIT_ON_EOD:
					if(getPos()>0)
						exitLongMarket();
					else
						exitShortMarket();
					logState("TRADE EVENT "+e);	
					break;
				case TRADE_EVENT.SWING_OUT_LONG:
					trade.type="SWING_OUT";
					trade.target=range.breakoutTarget;
					trade.stop=(tick.ask-range.low)*tf+tm*2;
					trade.signal="LongSwingOut";
					enterLongMarket();
					//enterLong(tick.bid);
					logState("TRADE EVENT "+e);	
					break;
				case TRADE_EVENT.SWING_OUT_SHORT:
					trade.type="SWING_OUT";
					trade.target=range.breakoutTarget;
					trade.stop=(range.high-tick.bid)*tf+tm*2;
					trade.signal="ShortSwingOut";
					enterShortMarket();
					//enterShort(tick.ask);
					logState("TRADE EVENT "+e);	
					break;
				case TRADE_EVENT.SWING_IN_LONG:
					if(range.lastSwingInDir<=0){
						trade.type="SWING_IN";
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
						trade.type="SWING_IN";
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
		protected  override void tickDetector(){
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
			if(pos>0&&!TradingLive){
				if(tick.bid>=trade.entry+trade.target/tf){
					processTradeEvent(TRADE_EVENT.EXIT_LONG);
				}
				else if(tick.bid<=trade.entry-trade.stop/tf){
					processTradeEvent(TRADE_EVENT.STOP_LONG);
				}
				else if(trade.type=="SWING_OUT"&&tick.ask<range.high-tm/tf&&tick.ask<trade.entry-2*tm/tf){
					log("SWING OUT BRAKE");
					processTradeEvent(TRADE_EVENT.EXIT_LONG);
				}
			}
			else if(pos<0&&!TradingLive){
				if(tick.ask<=trade.entry-trade.target/tf){
					processTradeEvent(TRADE_EVENT.EXIT_SHORT);
				}
				else if(tick.ask>=trade.entry+trade.stop/tf){
					processTradeEvent(TRADE_EVENT.STOP_SHORT);
				}
				else if(trade.type=="SWING_OUT"&&tick.bid>range.low+tm/tf&&tick.bid>trade.entry+2*tm/tf){
					log("SWING OUT BRAKE");
					processTradeEvent(TRADE_EVENT.EXIT_SHORT);
				}
			}
		}
		protected override int getExitTarget(){
			
				return (int)trade.target;
			
		}
		protected override int getExitStop(){
			if(getPos()>0){
				if(trade.type=="SWING_OUT"){
					return (int) Math.Max((int)(trade.entry-range.high)*tf+tm,2*tm);
				}
				else{
					return (int) trade.stop;
				}
			}
			else if (getPos()<0){
				if(trade.type=="SWING_OUT"){
					return(int) Math.Max((int)(range.low-trade.entry)*tf+tm,2*tm);
					
				}
				else{
					return (int) trade.stop;
				}
			}
			return 0;
		}
		protected override string dumpSessionParameters(){
			return "MaxRange="+maxRange+";MaxRangePeriod="+MaxRangePeriod+";MinRange="+minRange+";MinRangePeriod="+MinRangePeriod;
		}
		protected override string dumpState(){
			return  ":: RANGE:amplitude="+range.amplitude+";period="+range.period+";high="+range.high+";median="+range.median+";low="+range.low+";active="+range.active;
		}
        #region Properties
       
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
		
        #endregion
    }
	
	
}
