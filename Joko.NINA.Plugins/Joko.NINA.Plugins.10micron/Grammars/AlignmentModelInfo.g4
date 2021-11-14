grammar AlignmentModelInfo;

// ZZZ.ZZZZ,+AA.AAAA,EE.EEEE,PPP.PP,+OO.OOOO,+aa.aa,+bb.bb,NN,RRRRR.R#
alignmentModelInfo		: raAzimuth ',' raAltitude ',' paError ',' raPositionAngle ',' orthogonalityError ',' azimuthTurns ',' altitudeTurns ',' modelTerms ',' rmsError '#' ;

raAzimuth	:  INTEGER '.' INTEGER ;
raAltitude	:  sign INTEGER '.' INTEGER ;
paError	:  INTEGER '.' INTEGER ;
raPositionAngle	:  INTEGER '.' INTEGER ;
orthogonalityError	:  sign INTEGER '.' INTEGER ;
azimuthTurns	:  sign INTEGER '.' INTEGER ;
altitudeTurns	:  sign INTEGER '.' INTEGER ;
modelTerms	: INTEGER ;
rmsError	: INTEGER '.' INTEGER ;
sign	: SIGN ;

fragment DIGIT : [0-9] ;
INTEGER : DIGIT+ ;
SIGN : '-'|'+' ;
WHITESPACE : [ \t\r\n] -> skip ;
