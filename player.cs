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
    public class Player : Base
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
			public int bias;
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
			public double preaverage;																				//average of medians of n sticks prior to this one
			public double premin;
			public double premax;
		}
		public enum RANGE_EVENT {DETECT,BREAKOUT,BREAKOUT_CLOSE,END};	//END is for unknown reason stop on loss happened
		public enum TRADE_EVENT {EXIT_ON_EOD, SWING_IN_SHORT,SWING_IN_LONG, SWING_OUT_LONG,SWING_OUT_SHORT,EXIT_LONG_LONG_TRADE,EXIT_LONG_SHORT_TRADE,EXIT_LONG,EXIT_SHORT,STOP_LONG,STOP_SHORT,KICKASS_LONG,KICKASS_SHORT,BOUNCE_LONG,BOUNCE_SHORT};
		public enum TRADE_TYPE {SWING_IN, SWING_OUT,KICKASS};
		#endregion
		
        #region Variables
        //properties
		
		private double 	maxRange=10;
		private double 	minRange=4;
		private int 	maxPeriod=18;
		private int 	minPeriod=3;
		private int 	timeLimitTrade=120;
		private int     tm=1;	
		private bool    allowSwingOut=true;		
		private bool 	allowPreBounce=false;
		private bool	allowLongTradesKill=false;
		private bool    allowKickass=true;
		private bool 	allowBounce=true;
		private bool    allowGulfik=false;
		private int     maxLookBackEngulfik=9;
		private int 	maxExtendLookBackEngulfik=36;
		private int     minStop=6;
		private int     kickassStop=6;
		private int     kickassTarget=3;
		private int     iTime;

		bool 		virgin		=	true;																			//detects first live tick to reset any existing ranges
		Range		range		=	new Range();
		Candle[]    candles		=	new Candle[3];
		bool debug_gulfik=false;
		#endregion			
		protected override void start(){}		
		protected  override void barDetector(){	
			if(!Historical){
				if(virgin){
					virgin=false;
					//range.active=false;
					log("LIVE!");
				}
			}
			//logState("barDetector");
			iTime=ToTime(Time[0]);	
			
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
				detectRange();
				detectRangeBias();
			
			}else if(range.active) {
				detectRangeBias();
			}
			
			if(range.active&&(tick.bid>range.high||tick.ask<range.low)){
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
				if((pos>0/*&&tick.bid>trade.entry*/)||(pos<0/*&&tick.ask<trade.entry*/)){
					//log("POSITIVE OUT");
					if(pos>0)
						processTradeEvent(TRADE_EVENT.EXIT_LONG_LONG_TRADE);
					else
						processTradeEvent(TRADE_EVENT.EXIT_LONG_SHORT_TRADE);
				}
			}
			if((AllowLongTradesKill&&pos!=0&&bar-trade.enteredBar>6&&trade.type=="SWING_OUT"&&(tick.bid<trade.entry-3/tf||tick.ask>trade.entry+3/tf))){
				//log("POSITIVE OUT");
				trade.signal="giveup";
				if(pos>0)
					processTradeEvent(TRADE_EVENT.EXIT_LONG_LONG_TRADE);
				else
					processTradeEvent(TRADE_EVENT.EXIT_LONG_SHORT_TRADE);
			}
			
			if(pos==0&&allowKickass){
				loadCandlesticks();
				if(shpalyDownUp())
					processTradeEvent(TRADE_EVENT.KICKASS_LONG);
				else if(shpalyUpDown())
					processTradeEvent(TRADE_EVENT.KICKASS_SHORT);
				
			}
			if(allowGulfik){
				if(debug_gulfik)
					logState("DEBUG BEGIN GULFIK");	
				if(pos<0&&engulfik(0,1,0)){
					//trade.dir=1;
					//trade.target=12;
					//trade.stop=19;
					trade.signal="kickass gulfik downup exit";
					processTradeEvent(TRADE_EVENT.EXIT_SHORT);
				}
				else if(pos>0&&engulfik(0,-1,0)){
					//trade.target=12;
					//trade.stop=19;
					trade.signal="kickass gulfik updown exit";
					processTradeEvent(TRADE_EVENT.EXIT_LONG);
				}
			/*	else if(pos==0&&engulfik(0,1,0)){
					trade.dir=1;
					trade.target=3;
					trade.stop=6;
					trade.signal="kickass gulfik downup";
					processTradeEvent(TRADE_EVENT.KICKASS_LONG);
				}
				else if(pos==0&&engulfik(0,-1,0)){
					trade.dir=-1;
					trade.target=3;
					trade.stop=6;
					trade.signal="kickass gulfik updown";
					processTradeEvent(TRADE_EVENT.KICKASS_SHORT);
				}*/

			}
		}
		protected void detectRange(){
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
						if((abdn<rm&&abup>rm)){																// range detected
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
									//bounceTriggered=false;												//reset secondary (bounce) watch
									//breakRange=false;														//reset hard break of the range indicator
									//noEntry=false;
									range.active=true;														//set watch indicator
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
									/*if(gainTotal>0){
										DrawText( "tm2"+CurrentBars[1],true,"TOTAL: "+gainTotal.ToString("c") ,
										0,range.high+lineNum()*0.25,20,Color.Green, new Font("Ariel",8),
										StringAlignment.Near,Color.Transparent,Color.Beige, 0);
									}
									else{
										DrawText( "tm2"+CurrentBars[1],true,"TOTAL: "+gainTotal.ToString("c") ,
										0,range.high+lineNum()*0.25,20,Color.Red, new Font("Ariel",8),
										StringAlignment.Near,Color.Transparent,Color.Beige, 0);
									}*/
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
		protected void detectRangeBias(){
			double sbup=0, sbdn=0;
			int currentRangeLength=bar-range.startBar;
			for(int i=0;i<currentRangeLength;i++){
				double top=Math.Max(Opens[2][i],Closes[2][i]);
				double btm=Math.Min(Opens[3][i],Closes[3][i]);
				sbup+=top;       																	// high-end of the candle body
				sbdn+=btm;  
			}
			double abup=sbup/currentRangeLength;
			double abdn=sbdn/currentRangeLength;
			double abm=abdn+(abup-abdn)/2;
			if(abm>range.median+(range.high-range.median)/2)
				range.bias= 1;
			else if(abm<range.median-(range.median-range.low)/2)
				range.bias= -1;
			else	
				range.bias=0;		

		}
		//This method measures and deconstructs last three candles to be used by kickass indicators
		protected void loadCandlesticks(){
			for(int i=0;i<3;i++){
				candles[i]=new Candle();
				candles[i].high=Highs[0][i];																		//based on asks
				candles[i].low=Lows[0][i];																			//based on bids	
				candles[i].height=(int)((candles[i].high-candles[i].low)*tf);
				candles[i].top=Math.Max(Opens[0][i],Closes[0][i]);
				candles[i].bottom=Math.Min(Opens[0][i],Closes[0][i]);
				candles[i].body=(int)((candles[i].top-candles[i].bottom)*tf);
				candles[i].hair=(int)((candles[i].high-candles[i].top)*tf);
				candles[i].beard=(int)((candles[i].bottom-candles[i].low)*tf);
				if(Opens[0][i]>Closes[0][i])
					candles[i].dir=-1;
				else	
					candles[i].dir=1;
				double quarter=(candles[i].height/4)/tf;
				double bodyQuarter=(candles[i].body/4)/tf;
				candles[i].topQuarter=candles[i].high-quarter;
				candles[i].bottomQuarter=candles[i].low+quarter;
				candles[i].topBodyQuarter=candles[i].top-bodyQuarter;
				candles[i].bottomBodyQuarter=candles[i].bottom+bodyQuarter;
				candles[i].preaverage=0;
				int c=0;
				for(int j=i+1;j<i+8;j++){
					candles[i].preaverage+=Lows[0][j]+(Highs[0][j]-Lows[0][j])/2;
					c++;
				}
				
				candles[i].preaverage/=c;
				candles[i].premin=MIN(Lows[0],6)[i+i];
				candles[i].premax=MIN(Highs[0],6)[i+i];
			}
		}
		protected string dumpCandlesticks(){
			string output="";
			for(int i=0;i<3;i++){
				string s="";
				int j=0;
				if(i>0)
					j=15;
				for(int k=0;k<j;k++){
					s+=" ";
				}
				output+=s+"CANDLE["+i+"]: high="+candles[i].high+";low="+candles[i].low+";height="+candles[i].height+";top="+candles[i].top+
					";bottom="+candles[i].bottom+";body="+candles[i].body+";hair="+candles[i].hair+";beard="+candles[i].beard+";dir="+candles[i].dir+
					";topQuarter="+candles[i].topQuarter+";bottomQuarter="+candles[i].bottomQuarter+";topBodyQuarter="+candles[i].topBodyQuarter+
					";bottomBodyQuarter="+candles[i].bottomBodyQuarter+";preaverage="+candles[i].preaverage+";premin="+candles[i].premin+";premax="+candles[i].premax+"\n";
			}
			return output;
		}
	
		protected string dumpConditions(string name,Dictionary<string, bool> conds ){
			string output=name+" CONDS:";
			foreach (KeyValuePair<string, bool> pair in conds){
				output+=pair.Key+","+pair.Value+";";
			}
			return output;
		}
		protected bool evalConditions(String pattern,Dictionary<string, bool> conds ){
			string output="CONDS:";
			foreach (KeyValuePair<string, bool> pair in conds){
				if(!pair.Value){
					//log(pattern+" COND FAILED:"+pair.Key);
					return false;
				}
			}
			return true;
		}
		protected bool engulfik(int b, int dir, int r){
			if(debug_gulfik)
			 log("DEBUG: ENGULFIK b="+b+";dir="+dir+";r="+r+";Open="+Opens[0][b]+";Close="+Closes[0][b]);
			if(b>=maxLookBackEngulfik){																		
				return false;
			}
			
			if(engulfik(b+1,dir,r+1)){																			//check if the next bar is engulfik with a higher priority
				if(r>0){
					if(debug_gulfik)																						//if I am not the top level, just pass the result up	
						log("TRUE");
					return true;
				}
				else{
					if(debug_gulfik)
						log("FALSE");	
					return false;																				//if I am the top level, it is not good to have inner engulf
				}
			}
			if(debug_gulfik)
				log("GULFIK1 - PAST PRIMARY ITER r="+r);
			//now check if my bar is engulfik 	
			if(dir==1&&Opens[0][b]>Closes[0][b]){	
				if(debug_gulfik)															//cannot be an engufik if counter-direction color
					log("FALSE - counterdir, Opens[0][b]="+Opens[0][b]+";Closes[0][b]="+Closes[0][b]);
				return false;
			}
			else if (dir==-1&&Opens[0][b]<Closes[0][b]){
				if(debug_gulfik)
					log("FALSE");
				return false;
			}
			double top=Math.Max(Opens[0][b],Closes[0][b]);
			double bottom=Math.Min(Opens[0][b],Closes[0][b]);
			if(dir==1){
				if(engulfik2(b,dir,Opens[0][b],Closes[0][b],Closes[0][b],r)){							//secondary recursion to see if I am the winner
					if(debug_gulfik)
						log("TRUE");
					return true;
				}
				else{
					if(debug_gulfik)
						log("FALSE gulfik2 failed");
					return false;
				}
				
			}
			else{
				if(engulfik2(b,dir,Opens[0][b],Closes[0][b],Closes[0][b],r)){
					if(debug_gulfik)
						log("TRUE");
					return true;
				}
				else{
					if(debug_gulfik)
						log("FALSE");
					return false;
				}
			}			
		}
		protected bool engulfik2(int b, int dir, double low_water,double high_water,double level, int r){
			if(debug_gulfik)
				log("      DEBUG: ENGULFIK2 b="+b+";dir="+dir+";low_water="+low_water+";high_water="+high_water+";level="+level+";r="+r+";Open="+Opens[0][b]+";Close="+Closes[0][b]);

			
			//1. Check for new minimum (for long engulfik)
			//2. Check for new maximum  
			//3. Check if next bar top is above me;
			if(b>=maxLookBackEngulfik){	
				if(debug_gulfik)
					log("      FALSE");
				return false;
			}
			//my top and bot
			double lTop=Math.Max(Opens[0][b],Closes[0][b]);
			double lBot=Math.Min(Opens[0][b],Closes[0][b]);
			double lHigh=Highs[0][b];
			double lLow=Lows[0][b];

			//next bar
			double nBot=Math.Min(Opens[0][b+1],Closes[0][b+1]);
			double nTop=Math.Max(Opens[0][b+1],Closes[0][b+1]);
			double nHigh=Highs[0][b+1];
			double nLow=Lows[0][b+1];
				
			if(dir>0){
				low_water=Math.Min(low_water,lBot);
				high_water=Math.Max(high_water,lTop);
				if(nLow>lLow&&nTop>level&&level-low_water>4/tf){	//found first above the level
					
					if(engulfik3(b+1,dir,low_water,nTop,level)) {// 3d recursion to check if it will climb to twice level-lMin befor hitting new min
						if(r==0)	//if checking on behalf of top level primary recursion
							CandleOutlineColorSeries[b]=Color.Chartreuse;
						if(debug_gulfik)
							log("      TRUE - WINNER");
						return true;
					}
				}
				else{
					if(nTop<=level/tf&&engulfik2(b+1,dir,low_water,high_water,level,r)){
						if(r==0)
							CandleOutlineColorSeries[b]=Color.Chartreuse;
						if(debug_gulfik)
							log("      TRUE - WINNER");	
						return true;
					}else {
						if(debug_gulfik)
							log("      FALSE");
						return false;
					}
				}
			}
			else{
				low_water=Math.Max(low_water,lTop);
				high_water=Math.Min(high_water,lBot);
				if(nHigh<lHigh&&nBot<level&&low_water-level>4/tf){
					if(engulfik3(b+1,dir,low_water,nBot,level)){ // end scenario
						if(r==0)
							CandleOutlineColorSeries[b]=Color.Chartreuse;
						if(debug_gulfik)
							log("      TRUE - WINNER");
						return true;
					}
				}
				else {
					if(nBot>=level/tf&&engulfik2(b+1,dir,low_water,high_water,level,r)){
						if(r==0)
							CandleOutlineColorSeries[b]=Color.Chartreuse;
						if(debug_gulfik)
							log("      TRUE - WINNER");		
						return true;	
					}
					else{
						if(debug_gulfik)
							log("      FALSE");
						return false;
					}
					
				}
			}
			return false;
		}
		protected bool engulfik3(int b, int dir, double min,double max,double level){
		
			if(b>=maxExtendLookBackEngulfik) //max lookback
				return false;
			double lBot=Math.Min(Opens[0][b],Closes[0][b]);
			double lTop=Math.Max(Opens[0][b],Closes[0][b]);
			if(debug_gulfik)
				log("            DEBUG: ENGULFIK3 b="+b+";dir="+dir+";min="+min+";max="+max+";level="+level+";Open="+Opens[0][b]+";Close="+Closes[0][b]+";lBot="+lBot+";lTop="+lTop);
			if(dir>0){
				
				if(lTop>level&&lTop>min&&lTop-min>(level-min)*2){
					if(debug_gulfik)
						log("            TRUE");
					return true;
				}
				if(lBot<min){
					if(debug_gulfik)
						log("            FALSE");
					return false;
				}
				return engulfik3(b+1,dir,min,max,level);	
			}			
			else{
				if(lBot<level&&lBot<min&&min-lBot>(min-level)*2){
					if(debug_gulfik)
						log("            TRUE");
					return true;
				}
				
				if(lTop>min){
					if(debug_gulfik)
						log("            FALSE");
					return false;
				}
				return engulfik3(b+1,dir,min,max,level);	
			}
			return false;
		}
		protected bool shpalyDownUp(){
			const int PRE_PERIOD=6;																					//sticks
			const int FIRST_STICK_HEIGHT=4;																			//ticks
			const int SECOND_STICK_HEIGHT=3;																		//ticks
			const int FIRST_HAIRCUT=1;																				//ticks
			const int SECOND_HAIRCUT=2;																				//ticks
			
			Dictionary<string, bool> conds =new Dictionary<string, bool>();
			conds.Add("FirstRed",candles[1].dir==-1);
			conds.Add("SecondGreen",candles[0].dir==1);
			conds.Add("FirstHeight",candles[1].height>=FIRST_STICK_HEIGHT);
			conds.Add("SecondHeight",candles[0].height>=SECOND_STICK_HEIGHT);
			conds.Add("StickingDown",candles[1].topBodyQuarter<=Math.Min(MIN(Opens[0],PRE_PERIOD)[2],MIN(Closes[0],PRE_PERIOD)[2]));
			//	conds.Add("SecondCloseGTEQFirstBodyTop",candles[0].top>=candles[1].topBodyQuarter);
			conds.Add("TopsMatch",candles[0].top >= candles[1].top);
			conds.Add("SharpDown",candles[2].top>=candles[1].top+(candles[2].body)/tf&&candles[1].low<candles[2].low-(candles[1].body/2)/tf);
			conds.Add("Preaverage",candles[1].preaverage>=candles[1].top+(candles[1].body)/tf&&candles[1].premax>candles[1].high+(candles[1].height*2)/tf);
			//conds.Add("Preaverage",preaverage>=candles[0].top+candles[1].body/tf);
			conds.Add("FirstHaircut",candles[1].hair<=1);
			conds.Add("SecondHaircut",candles[0].hair<=1);
			conds.Add("RelativeHeights",candles[0].height<=candles[1].height+2);
			conds.Add("RelativeBodies",candles[0].body>=candles[1].body);
			if(candles[1].body>0&&candles[1].beard>0)
				conds.Add("BodyBeardInverse",candles[1].beard/3>=1/candles[1].body);
			
			//logState(dumpCandlesticks()+dumpConditions("ShpalyDownUp",conds));
			if(evalConditions("ShpalyDownUp",conds)){
				/*if(candles[1].height>=20){
					trade.target=20;
					trade.stop=12;
				}
				else if(candles[1].height>=12){
					trade.target=12;
					trade.stop=12;
				}
				else {
					trade.target=4;
					trade.stop=12;
				}*/
				trade.target=kickassTarget;
				trade.stop=kickassStop;
				trade.signal="kickass shpaly downup";
				return true;
				
			}
			return false;
		}
		protected bool shpalyUpDown(){
			const int PRE_PERIOD=6;																					//sticks
			const int FIRST_STICK_HEIGHT=4;																			//ticks
			const int SECOND_STICK_HEIGHT=3;																		//ticks
			const int FIRST_BEARD=1;																				//ticks
			const int SECOND_BEARD=2;																				//ticks
			
			//log("DEBUG prevCandleDir="+prevCandleDir+";candleDir="+candleDir+";candleHeight="+candleHeight+";prevCandleBody="+prevCandleBody+";prevCandleTop="+prevCandleTop+";prevCandleBottom="+prevCandleBottom+";body_up_tip="+body_up_tip+";body_down_tip="+body_down_tip+";prevCandleHeight="+prevCandleHeight+";down_tip="+down_tip+";up_tip="+up_tip+";Close[0]="+Close[0]+";Math.Min(MIN(Opens[2],3)[2],MIN(Closes[2],3)[2])="+Math.Min(MIN(Opens[2],3)[2],MIN(Closes[2],3)[2]));
			Dictionary<string, bool> conds =new Dictionary<string, bool>();
			conds.Add("FirstGreen",candles[1].dir==1);
			conds.Add("SecondRed",candles[0].dir==-1);
			conds.Add("FirstHeight",candles[1].height>=FIRST_STICK_HEIGHT);
			conds.Add("SecondHeight",candles[0].height>=SECOND_STICK_HEIGHT);
			conds.Add("StickingUp",candles[1].bottomBodyQuarter>=Math.Max(MAX(Opens[0],PRE_PERIOD)[2],MAX(Closes[0],PRE_PERIOD)[2]));
			//	conds.Add("SecondCloseLTEQFirstBodyBottom",candles[0].bottom<=candles[1].bottomBodyQuarter);
			conds.Add("bottomsMatch",candles[0].bottom<=candles[1].bottom);
			
			conds.Add("SharpUp",candles[2].bottom<=candles[1].bottom-(candles[2].body)/tf&&candles[1].high>candles[2].high+(candles[1].body/2)/tf);
			conds.Add("Preaverage",candles[1].preaverage<=candles[1].bottom-(candles[1].body)/tf&&candles[1].premin<candles[1].low-(candles[1].height*2)/tf);
			
			//conds.Add("Preaverage",preaverage<=candles[0].bottom-candles[1].body/tf);
			
			conds.Add("FirstBeard",candles[1].beard<=1);
			conds.Add("SecondBeard",candles[0].beard<=1);
			conds.Add("RelativeHeights",candles[0].height<=candles[1].height+2);
			conds.Add("RelativeBodies",candles[0].body>=candles[1].body);
			if(candles[1].body>0&&candles[1].hair>0)
				conds.Add("BodyHairInverse",candles[1].hair/3>=1/candles[1].body);
			//logState(dumpConditions("ShpalyUpDown" ,conds));
			if(evalConditions("ShpalyUpDown",conds)){
				/*if(candles[1].height>=20){
					trade.target=20;
					trade.stop=12;
				}
				else if(candles[1].height>=12){
					trade.target=12;
					trade.stop=12;
				}
				else {
					trade.target=4;
					trade.stop=12;
				}*/
				trade.target=kickassTarget;
				trade.stop=kickassStop;

				trade.signal="kickass shpaly updown";
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
			log("processTradeEvent trade.pending="+trade.pending);
			if(trade.pending)
				return;
			switch(e){
				case TRADE_EVENT.BOUNCE_LONG:
				case TRADE_EVENT.KICKASS_LONG:
					trade.dir=1;
					trade.type="KICKASS";
					if(Historical)
						enterLongMarket();
					else
						enterLong(tick.ask);
					logState("TRADE EVENT "+e);	
					break;
				case TRADE_EVENT.BOUNCE_SHORT:
				case TRADE_EVENT.KICKASS_SHORT:
					trade.dir=-1;
					trade.type="KICKASS";
					if(Historical)
						enterShortMarket();
					else
						enterShort(tick.bid);	
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
					if(range.bias==-1){
						log("RANGE BIAS IN CONFLICT, NO TRADE");
						return;
					}
					trade.dir=1;
					trade.type="SWING_OUT";
					trade.target=range.breakoutTarget;
					trade.stop=19;//(tick.ask-range.low)*tf+tm*2;
					trade.signal="LongSwingOut";
					if(Historical)
						enterLongMarket();
					else
						enterLong(tick.ask);
					//enterLong(tick.bid);
					logState("TRADE EVENT "+e);	
					break;
				case TRADE_EVENT.SWING_OUT_SHORT:
					if(range.bias==1){
						log("RANGE BIAS IN CONFLICT, NO TRADE");
						return;
					}
					trade.dir=-1;
					trade.type="SWING_OUT";
					trade.target=range.breakoutTarget;
					trade.stop=19;//(range.high-tick.bid)*tf+tm*2;
					trade.signal="ShortSwingOut";
					if(Historical)
						enterShortMarket();
					else
						enterShort(tick.bid);	
					//enterShort(tick.ask);
					logState("TRADE EVENT "+e);	
					break;
				case TRADE_EVENT.SWING_IN_LONG:
					if(range.lastSwingInDir<=0){
						trade.dir=1;
						trade.type="SWING_IN";
						trade.signal="LongSwingIn";
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
						trade.dir=-1;
						trade.type="SWING_IN";
						trade.signal="ShortSwingIn";
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
					if(allowSwingOut&&tick.bid==range.high+tm/tf+0/tf/*&&range.high>=MAX(Highs[0],24)[1]*/){
						if(!range.breakout)
							processTradeEvent(TRADE_EVENT.SWING_OUT_LONG);
						range.breakout=true;
					}
					else if(allowSwingOut&&tick.ask==range.low-tm/tf-0/tf/*&&range.low<=MIN(Lows[0],24)[1]*/){
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
			/*if(pos>0&&!TradingLive){
				
				if((tick.bid>=trade.entry+trade.target/tf)||(Closes[1][0]-1/tf>=trade.entry+trade.target/tf)){
					processTradeEvent(TRADE_EVENT.EXIT_LONG);
				}
				else if((tick.bid<=trade.entry-trade.stop/tf)||(Closes[1][0]+1/tf<=trade.entry-trade.stop/tf)){
					processTradeEvent(TRADE_EVENT.STOP_LONG);
				}
				else if(trade.type=="SWING_OUT"&&(tick.ask<range.high-tm/tf||Closes[1][0]-1/tf<range.high-tm/tf)&&(tick.ask<trade.entry-6*tm/tf||Closes[1][0]-1/tf<trade.entry-6*tm/tf)){
					log("SWING OUT BRAKE");
					processTradeEvent(TRADE_EVENT.EXIT_LONG);
				}
			}
			else if(pos<0&&!TradingLive){
				if(tick.ask<=trade.entry-trade.target/tf||Closes[1][0]+1/tf<=trade.entry-trade.target/tf){
					processTradeEvent(TRADE_EVENT.EXIT_SHORT);
				}
				else if(tick.ask>=trade.entry+trade.stop/tf||Closes[1][0]-1/tf>=trade.entry+trade.stop/tf){
					processTradeEvent(TRADE_EVENT.STOP_SHORT);
				}
				else if(trade.type=="SWING_OUT"&&(tick.bid>range.low+tm/tf||Closes[1][0]+1/tf>range.low+tm/tf)&&(tick.bid>trade.entry+6*tm/tf||Closes[1][0]+1/tf>trade.entry+6*tm/tf)){
					log("SWING OUT BRAKE");
					processTradeEvent(TRADE_EVENT.EXIT_SHORT);
				}
			}*/
		}
		protected override void reportLoss(){
			if(!allowBounce)
				return;
			if(getPos()==0&&(ToTime(Time[0])>=iLastEntryTime&&ToTime(Time[0])<iRestartTime)){
				return;
			}
			if(trade.dir>0&&(true||trade.signal=="LongSwingOut")){
				trade.target=trade.stop;
				trade.stop=minStop;
				trade.signal="bounce down";
				processTradeEvent(TRADE_EVENT.BOUNCE_SHORT);
			}
			else if(trade.dir<0&&(true||trade.signal=="ShortSwingOut")){
				trade.target=trade.stop;
				trade.stop=minStop;
				trade.signal="bounce up";
				processTradeEvent(TRADE_EVENT.BOUNCE_LONG);			
			}
		}
		protected override void reportWin(){
		}
		protected override int getExitTarget(){
			
				return (int)trade.target;
			
		}
		protected override int getExitStop(){
			if(trade.dir>0){
				if(trade.type=="SWING_OUT"){
					trade.stop= (int) Math.Max((int)(tick.ask-range.low)*tf+tm,minStop*tm);
				}
				return (int) trade.stop;
			}
			else if (trade.dir<0){
				if(trade.type=="SWING_OUT"){
					trade.stop=(int) Math.Max((int)(range.high-tick.bid)*tf+tm,minStop*tm);
					
				}
				return (int) trade.stop;
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
			[Description("Enables allowKickass indicator")]
	        [GridCategory("Parameters")]
	        public bool AllowBounce
	        {
	            get { return allowBounce; }
	            set {  allowBounce= value; }
	        }
			
			[Description("Enables Gulfik indicator")]
	        [GridCategory("Parameters")]
	        public bool AllowGulfik
	        {
	            get { return allowGulfik; }
	            set {  allowGulfik= value; }
	        }
			[Description("How far to go looking for gulfik")]
	        [GridCategory("Parameters")]
	        public int MaxLookBackEngulfik
	        {
	            get { return maxLookBackEngulfik; }
	            set {  maxLookBackEngulfik= value; }
	        }
	        [Description("How far to go looking for the peak before gulfik")]
	        [GridCategory("Parameters")]
	        public int MaxExtendLookBackEngulfik
	        {
	            get { return maxExtendLookBackEngulfik; }
	            set {  maxExtendLookBackEngulfik= value; }
	        }
			[Description("Minimim Stop")]
	        [GridCategory("Parameters")]
	        public int MinStop
	        {
	            get { return minStop; }
	            set {  minStop= value; }
	        }
			[Description("kickassStop")]
	        [GridCategory("Parameters")]
	        public int KickassStop
	        {
	            get { return kickassStop; }
	            set {  kickassStop= value; }
	        }
	        [Description("KickassTarget")]
	        [GridCategory("Parameters")]
	        public int KickassTarget
	        {
	            get { return kickassTarget; }
	            set {  kickassTarget= value; }
	        }
        #endregion
    }
	
	
}
