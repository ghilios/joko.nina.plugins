grammar AlignmentStarInfo;

// HH:MM:SS.SS,+dd*mm:ss.s,eeee.e#
alignmentStarInfo		: time ',' angle ',' error '#' ;

time	:  hours ':' minutes ':' seconds '.' hundredthSeconds ;
angle	:  sign degrees '*' minutes ':' seconds '.' tenthSeconds ;
error	: errorArcseconds '.' errorTenthArcseconds ;

sign : SIGN ;
hours : INTEGER ;
minutes : INTEGER ;
seconds : INTEGER ;
hundredthSeconds : INTEGER ;
degrees : INTEGER ;
tenthSeconds : INTEGER ;
errorArcseconds : INTEGER ;
errorTenthArcseconds : INTEGER ;

fragment DIGIT : [0-9] ;
INTEGER : DIGIT+ ;
SIGN : '-'|'+' ;
WHITESPACE : [ \t\r\n] -> skip ;
