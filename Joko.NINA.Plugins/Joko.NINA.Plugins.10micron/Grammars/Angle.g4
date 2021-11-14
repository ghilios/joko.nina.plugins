grammar Angle;

angle :  sign degrees ':' minutes ':' seconds '.' tenth_seconds '#' 
	  | sign degrees '*' minutes ':' seconds '#'
	  | sign degrees '*' minutes '#'
      ;
sign : SIGN ;
degrees : INTEGER ;
minutes : INTEGER ;
seconds : INTEGER ;
tenth_seconds : INTEGER ;

fragment DIGIT : [0-9] ;
SIGN : '-'|'+' ;
INTEGER : DIGIT+ ;