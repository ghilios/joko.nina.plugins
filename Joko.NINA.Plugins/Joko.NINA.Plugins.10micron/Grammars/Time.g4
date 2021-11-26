grammar Time;

// HH:MM.M# 
// HH:MM:SS#
// HH:MM:SS.S#
// HH:MM:SS.SS#

time  :  hours ':' minutes ':' tenth_minutes '#' 
	  | hours ':' minutes ':' seconds '#'
	  | hours ':' minutes ':' seconds '.' tenth_seconds '#'
      | hours ':' minutes ':' seconds '.' hundredth_seconds '#'
      ;

hours : INTEGER ;
minutes : INTEGER ;
seconds : INTEGER ;
tenth_minutes : INTEGER ;
hundredth_seconds : INTEGER ;
tenth_seconds : INTEGER ;

fragment DIGIT : [0-9] ;
INTEGER : DIGIT+ ;