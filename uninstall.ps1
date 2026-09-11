# WireFox Uninstaller Script
# https://github.com/TalviFox/WireFox
# Cleanly removes WireFox background tasks, shortcuts, registry entries, and program files.

[CmdletBinding()]
param(
    [switch]$Force,
    [switch]$Silent
)

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$ErrorActionPreference = "SilentlyContinue"

$OutputEncoding = [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$esc = [char]27
$u = [char]0x2580
$d = [char]0x2584

$banner = @"
$esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[38;2;7;5;5m$esc[49m$d$esc[38;2;23;30;35m$esc[49m$d$esc[38;2;0;0;0m$esc[48;2;39;78;98m$u$esc[38;2;13;20;24m$esc[48;2;117;163;184m$u$esc[38;2;14;21;26m$esc[48;2;107;153;176m$u$esc[38;2;0;0;0m$esc[48;2;39;79;99m$u$esc[38;2;23;30;34m$esc[49m$d$esc[38;2;7;5;4m$esc[49m$d$esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[0m   
$esc[0m $esc[0m $esc[0m $esc[0m $esc[38;2;0;0;0m$esc[49m$d$esc[38;2;0;0;0m$esc[49m$d$esc[38;2;17;25;29m$esc[49m$d$esc[38;2;0;0;0m$esc[48;2;46;72;88m$u$esc[38;2;4;2;2m$esc[48;2;53;88;114m$u$esc[38;2;33;48;57m$esc[48;2;34;51;63m$u$esc[38;2;48;78;98m$esc[48;2;33;73;114m$u$esc[38;2;48;52;72m$esc[48;2;28;71;122m$u$esc[38;2;63;134;173m$esc[48;2;18;54;92m$u$esc[38;2;52;57;78m$esc[48;2;16;71;99m$u$esc[38;2;86;114;143m$esc[48;2;38;41;57m$u$esc[38;2;75;104;135m$esc[48;2;38;42;57m$u$esc[38;2;52;57;78m$esc[48;2;16;73;99m$u$esc[38;2;62;131;170m$esc[48;2;17;55;93m$u$esc[38;2;48;52;72m$esc[48;2;28;70;120m$u$esc[38;2;47;77;98m$esc[48;2;32;72;112m$u$esc[38;2;31;47;56m$esc[48;2;32;51;62m$u$esc[38;2;2;2;2m$esc[48;2;52;89;115m$u$esc[38;2;0;0;0m$esc[48;2;45;71;87m$u$esc[38;2;17;25;29m$esc[49m$d$esc[38;2;0;0;0m$esc[49m$d$esc[38;2;0;0;0m$esc[49m$d$esc[0m $esc[0m $esc[0m $esc[0m $esc[0m   $([char]::ConvertFromUtf32(0x1F98A)) $esc[1;33mWireFox Uninstaller$esc[0m
$esc[38;2;9;12;14m$esc[49m$d$esc[38;2;0;0;0m$esc[48;2;52;83;104m$u$esc[38;2;12;15;15m$esc[48;2;61;109;145m$u$esc[38;2;30;44;51m$esc[48;2;46;51;69m$u$esc[38;2;45;74;94m$esc[48;2;39;88;139m$u$esc[38;2;55;97;126m$esc[48;2;25;59;111m$u$esc[38;2;47;52;71m$esc[48;2;18;40;84m$u$esc[38;2;43;92;143m$esc[48;2;10;48;84m$u$esc[38;2;14;32;56m$esc[48;2;11;26;33m$u$esc[38;2;26;23;19m$esc[48;2;75;69;69m$u$esc[38;2;13;17;26m$esc[48;2;40;35;32m$u$esc[38;2;11;74;95m$esc[48;2;8;88;98m$u$esc[38;2;51;56;76m$esc[48;2;42;46;63m$u$esc[38;2;49;53;73m$esc[48;2;11;68;96m$u$esc[38;2;47;51;70m$esc[48;2;11;74;101m$u$esc[38;2;39;43;59m$esc[48;2;17;43;79m$u$esc[38;2;44;48;66m$esc[48;2;18;36;74m$u$esc[38;2;49;54;74m$esc[48;2;20;79;114m$u$esc[38;2;11;74;95m$esc[48;2;9;76;88m$u$esc[38;2;14;17;26m$esc[48;2;39;37;34m$u$esc[38;2;26;23;19m$esc[48;2;74;69;68m$u$esc[38;2;14;33;58m$esc[48;2;11;27;33m$u$esc[38;2;42;92;143m$esc[48;2;10;48;84m$u$esc[38;2;47;52;71m$esc[48;2;18;39;84m$u$esc[38;2;55;97;126m$esc[48;2;26;60;111m$u$esc[38;2;46;75;94m$esc[48;2;39;88;140m$u$esc[38;2;30;43;51m$esc[48;2;46;50;69m$u$esc[38;2;12;14;16m$esc[48;2;61;109;146m$u$esc[38;2;0;0;0m$esc[48;2;52;83;104m$u$esc[38;2;9;12;15m$esc[49m$d$esc[0m   $esc[90mClean & Complete System Removal$esc[0m
$esc[38;2;3;6;11m$esc[48;2;2;4;8m$u$esc[38;2;37;81;122m$esc[48;2;20;56;99m$u$esc[38;2;44;87;130m$esc[48;2;29;64;107m$u$esc[38;2;14;39;81m$esc[48;2;43;47;64m$u$esc[38;2;12;46;80m$esc[48;2;60;66;90m$u$esc[38;2;12;75;98m$esc[48;2;48;53;72m$u$esc[38;2;42;46;63m$esc[48;2;50;55;75m$u$esc[38;2;9;101;107m$esc[48;2;1;52;61m$u$esc[38;2;43;57;57m$esc[48;2;65;42;42m$u$esc[38;2;155;125;125m$esc[48;2;185;145;140m$u$esc[38;2;62;59;59m$esc[48;2;182;173;175m$u$esc[38;2;17;15;18m$esc[48;2;91;90;86m$u$esc[38;2;8;53;75m$esc[48;2;3;37;54m$u$esc[38;2;13;87;116m$esc[48;2;10;50;67m$u$esc[38;2;13;91;118m$esc[48;2;1;34;46m$u$esc[38;2;19;58;95m$esc[48;2;4;22;39m$u$esc[38;2;21;54;93m$esc[48;2;14;33;56m$u$esc[38;2;12;30;58m$esc[48;2;4;18;40m$u$esc[38;2;17;11;16m$esc[48;2;91;93;89m$u$esc[38;2;63;60;60m$esc[48;2;182;173;175m$u$esc[38;2;155;125;125m$esc[48;2;185;145;140m$u$esc[38;2;43;57;57m$esc[48;2;64;42;41m$u$esc[38;2;9;103;109m$esc[48;2;1;38;50m$u$esc[38;2;42;46;63m$esc[48;2;47;52;71m$u$esc[38;2;12;75;98m$esc[48;2;48;52;72m$u$esc[38;2;13;46;80m$esc[48;2;60;66;90m$u$esc[38;2;14;39;82m$esc[48;2;43;47;64m$u$esc[38;2;44;88;131m$esc[48;2;29;64;106m$u$esc[38;2;37;80;122m$esc[48;2;19;56;98m$u$esc[38;2;3;6;10m$esc[48;2;2;4;8m$u$esc[0m   
$esc[38;2;3;5;10m$esc[48;2;1;2;4m$u$esc[38;2;25;61;104m$esc[48;2;13;42;84m$u$esc[38;2;26;78;121m$esc[48;2;25;77;121m$u$esc[38;2;53;58;79m$esc[48;2;48;52;72m$u$esc[38;2;41;45;62m$esc[48;2;44;48;66m$u$esc[38;2;46;51;69m$esc[48;2;57;62;85m$u$esc[38;2;16;97;124m$esc[48;2;52;57;78m$u$esc[38;2;1;50;59m$esc[48;2;1;45;54m$u$esc[38;2;148;92;98m$esc[48;2;155;120;120m$u$esc[38;2;195;150;145m$esc[48;2;190;145;140m$u$esc[38;2;220;200;195m$esc[48;2;185;145;140m$u$esc[38;2;145;151;150m$esc[48;2;163;155;150m$u$esc[38;2;0;6;12m$esc[48;2;36;14;5m$u$esc[38;2;34;22;17m$esc[48;2;77;45;32m$u$esc[38;2;67;32;19m$esc[48;2;103;60;42m$u$esc[38;2;59;25;10m$esc[48;2;136;103;89m$u$esc[38;2;28;16;12m$esc[48;2;97;70;58m$u$esc[38;2;0;1;9m$esc[48;2;34;13;4m$u$esc[38;2;145;151;150m$esc[48;2;163;155;150m$u$esc[38;2;220;200;195m$esc[48;2;185;145;140m$u$esc[38;2;195;150;145m$esc[48;2;190;145;140m$u$esc[38;2;147;86;92m$esc[48;2;155;120;120m$u$esc[38;2;0;65;73m$esc[48;2;0;20;23m$u$esc[38;2;54;59;81m$esc[48;2;8;47;54m$u$esc[38;2;47;51;70m$esc[48;2;9;53;74m$u$esc[38;2;43;47;64m$esc[48;2;12;84;113m$u$esc[38;2;54;59;81m$esc[48;2;50;55;75m$u$esc[38;2;26;78;122m$esc[48;2;25;75;118m$u$esc[38;2;25;61;104m$esc[48;2;12;42;82m$u$esc[38;2;2;5;9m$esc[48;2;1;2;5m$u$esc[0m   $esc[1;35m"Was it something I said...?"$esc[0m $esc[38;2;210;130;140m</3$esc[0m
$esc[38;2;25;26;27m$esc[48;2;16;18;21m$u$esc[38;2;79;86;110m$esc[48;2;162;164;175m$u$esc[38;2;21;72;104m$esc[48;2;95;148;161m$u$esc[38;2;50;55;75m$esc[48;2;44;48;66m$u$esc[38;2;42;46;63m$esc[48;2;46;51;69m$u$esc[38;2;57;63;86m$esc[48;2;10;51;60m$u$esc[38;2;53;58;79m$esc[48;2;5;17;26m$u$esc[38;2;0;28;42m$esc[48;2;26;22;19m$u$esc[38;2;150;115;115m$esc[48;2;165;130;130m$u$esc[38;2;185;140;135m$esc[48;2;180;140;135m$u$esc[38;2;210;185;180m$esc[48;2;140;117;105m$u$esc[38;2;187;171;163m$esc[48;2;115;80;66m$u$esc[38;2;86;38;20m$esc[48;2;90;48;32m$u$esc[38;2;94;53;36m$esc[48;2;90;47;31m$u$esc[38;2;101;62;47m$esc[48;2;103;63;48m$u$esc[38;2;255;255;255m$esc[48;2;245;242;241m$u$esc[38;2;174;154;146m$esc[48;2;232;227;225m$u$esc[38;2;66;13;0m$esc[48;2;181;163;156m$u$esc[38;2;191;175;166m$esc[48;2;109;74;59m$u$esc[38;2;210;185;180m$esc[48;2;163;145;135m$u$esc[38;2;185;140;135m$esc[48;2;190;145;145m$u$esc[38;2;150;115;120m$esc[48;2;160;125;125m$u$esc[38;2;27;12;5m$esc[48;2;76;43;26m$u$esc[38;2;57;20;8m$esc[48;2;68;34;21m$u$esc[38;2;2;42;59m$esc[48;2;5;73;86m$u$esc[38;2;13;84;115m$esc[48;2;45;49;67m$u$esc[38;2;52;57;78m$esc[48;2;46;51;69m$u$esc[38;2;20;72;103m$esc[48;2;96;150;164m$u$esc[38;2;78;84;107m$esc[48;2;163;164;175m$u$esc[38;2;25;26;28m$esc[48;2;16;18;21m$u$esc[0m   
$esc[38;2;2;3;8m$esc[48;2;1;3;8m$u$esc[38;2;89;109;134m$esc[48;2;27;80;119m$u$esc[38;2;84;175;186m$esc[48;2;58;63;87m$u$esc[38;2;52;57;78m$esc[48;2;9;94;99m$u$esc[38;2;44;48;66m$esc[48;2;24;17;16m$u$esc[38;2;7;36;38m$esc[48;2;53;32;26m$u$esc[38;2;72;32;18m$esc[48;2;97;53;35m$u$esc[38;2;85;44;27m$esc[48;2;202;188;183m$u$esc[38;2;209;186;183m$esc[48;2;251;253;253m$u$esc[38;2;248;247;247m$esc[48;2;245;242;240m$u$esc[38;2;90;47;31m$esc[48;2;106;65;50m$u$esc[38;2;88;44;27m$esc[48;2;95;51;37m$u$esc[38;2;95;54;37m$esc[48;2;83;39;22m$u$esc[38;2;93;51;35m$esc[48;2;163;139;129m$u$esc[38;2;109;73;58m$esc[48;2;238;235;233m$u$esc[38;2;242;238;237m$esc[48;2;254;253;253m$u$esc[38;2;255;255;255m$esc[48;2;241;236;234m$u$esc[38;2;225;218;215m$esc[48;2;108;73;58m$u$esc[38;2;83;38;21m$esc[48;2;91;49;30m$u$esc[38;2;93;52;36m$esc[48;2;100;57;41m$u$esc[38;2;236;232;230m$esc[48;2;108;69;54m$u$esc[38;2;139;99;88m$esc[48;2;222;219;215m$u$esc[38;2;98;54;36m$esc[48;2;178;156;147m$u$esc[38;2;61;23;13m$esc[48;2;54;15;5m$u$esc[38;2;5;76;87m$esc[48;2;9;67;84m$u$esc[38;2;47;51;70m$esc[48;2;44;48;66m$u$esc[38;2;46;50;69m$esc[48;2;58;63;87m$u$esc[38;2;95;184;194m$esc[48;2;57;63;86m$u$esc[38;2;91;110;134m$esc[48;2;27;80;119m$u$esc[38;2;2;3;8m$esc[48;2;1;3;7m$u$esc[0m   $esc[1;37mLeaving the den so soon?$esc[0m
$esc[38;2;2;3;8m$esc[48;2;1;3;5m$u$esc[38;2;31;88;126m$esc[48;2;29;82;116m$u$esc[38;2;57;63;86m$esc[48;2;61;67;91m$u$esc[38;2;2;17;24m$esc[48;2;46;51;69m$u$esc[38;2;84;45;29m$esc[48;2;81;104;99m$u$esc[38;2;98;51;33m$esc[48;2;141;120;116m$u$esc[38;2;76;32;14m$esc[48;2;209;197;191m$u$esc[38;2;218;210;206m$esc[48;2;246;244;243m$u$esc[38;2;247;245;244m$esc[48;2;143;117;104m$u$esc[38;2;218;210;207m$esc[48;2;88;39;29m$u$esc[38;2;32;24;18m$esc[48;2;93;134;107m$u$esc[38;2;22;25;16m$esc[48;2;0;93;32m$u$esc[38;2;16;10;6m$esc[48;2;17;64;32m$u$esc[38;2;149;138;135m$esc[48;2;113;130;120m$u$esc[38;2;255;255;255m$esc[48;2;254;254;254m$u$esc[38;2;255;255;255m$esc[48;2;255;255;255m$u$esc[38;2;154;147;143m$esc[48;2;114;119;137m$u$esc[38;2;14;7;6m$esc[48;2;0;12;76m$u$esc[38;2;25;21;30m$esc[48;2;63;80;155m$u$esc[38;2;27;13;13m$esc[48;2;107;114;153m$u$esc[38;2;86;44;27m$esc[48;2;92;50;27m$u$esc[38;2;246;244;243m$esc[48;2;230;224;223m$u$esc[38;2;185;165;157m$esc[48;2;233;227;224m$u$esc[38;2;25;0;0m$esc[48;2;194;189;186m$u$esc[38;2;0;23;29m$esc[48;2;179;168;167m$u$esc[38;2;44;48;66m$esc[48;2;23;50;48m$u$esc[38;2;45;50;68m$esc[48;2;45;49;67m$u$esc[38;2;53;58;80m$esc[48;2;62;68;93m$u$esc[38;2;30;89;127m$esc[48;2;29;80;114m$u$esc[38;2;2;3;8m$esc[48;2;1;2;5m$u$esc[0m   $esc[90mWe'll remove scheduled tasks, shortcuts & binaries.$esc[0m
$esc[38;2;0;0;0m$esc[48;2;0;0;0m$u$esc[38;2;18;42;68m$esc[48;2;13;26;53m$u$esc[38;2;52;57;78m$esc[48;2;43;47;65m$u$esc[38;2;52;57;78m$esc[48;2;55;61;83m$u$esc[38;2;78;101;103m$esc[48;2;55;44;49m$u$esc[38;2;114;111;112m$esc[48;2;79;83;82m$u$esc[38;2;220;223;224m$esc[48;2;92;92;92m$u$esc[38;2;247;248;248m$esc[48;2;224;224;224m$u$esc[38;2;163;141;132m$esc[48;2;255;255;255m$u$esc[38;2;126;91;80m$esc[48;2;255;255;255m$u$esc[38;2;222;233;224m$esc[48;2;255;255;255m$u$esc[38;2;100;184;129m$esc[48;2;255;255;255m$u$esc[38;2;42;112;152m$esc[48;2;255;255;255m$u$esc[38;2;184;201;189m$esc[48;2;255;255;255m$u$esc[38;2;252;252;252m$esc[48;2;255;255;255m$u$esc[38;2;252;252;252m$esc[48;2;255;255;255m$u$esc[38;2;189;192;208m$esc[48;2;255;255;255m$u$esc[38;2;48;118;168m$esc[48;2;255;255;255m$u$esc[38;2;128;148;221m$esc[48;2;255;255;255m$u$esc[38;2;223;223;233m$esc[48;2;255;255;254m$u$esc[38;2;135;105;91m$esc[48;2;255;255;255m$u$esc[38;2;232;227;225m$esc[48;2;252;252;252m$u$esc[38;2;255;255;255m$esc[48;2;255;255;255m$u$esc[38;2;237;238;238m$esc[48;2;104;104;103m$u$esc[38;2;205;198;198m$esc[48;2;0;0;0m$u$esc[38;2;30;79;82m$esc[48;2;50;55;75m$u$esc[38;2;52;57;78m$esc[48;2;58;64;87m$u$esc[38;2;51;56;76m$esc[48;2;42;46;63m$u$esc[38;2;18;41;68m$esc[48;2;13;26;52m$u$esc[38;2;0;0;0m$esc[48;2;0;0;0m$u$esc[0m   
$esc[0m $esc[38;2;8;19;33m$esc[48;2;1;4;6m$u$esc[38;2;28;71;120m$esc[48;2;23;46;86m$u$esc[38;2;58;64;87m$esc[48;2;54;59;81m$u$esc[38;2;44;59;66m$esc[48;2;42;104;104m$u$esc[38;2;93;60;49m$esc[48;2;97;41;24m$u$esc[38;2;77;76;76m$esc[48;2;63;63;61m$u$esc[38;2;133;134;134m$esc[48;2;84;85;86m$u$esc[38;2;150;149;149m$esc[48;2;88;88;88m$u$esc[38;2;160;159;159m$esc[48;2;105;105;105m$u$esc[38;2;250;250;250m$esc[48;2;245;245;244m$u$esc[38;2;253;253;253m$esc[48;2;250;250;250m$u$esc[38;2;42;112;152m$esc[48;2;251;252;251m$u$esc[38;2;170;170;170m$esc[48;2;132;132;132m$u$esc[38;2;93;93;93m$esc[48;2;0;0;0m$u$esc[38;2;93;93;93m$esc[48;2;0;0;0m$u$esc[38;2;171;171;171m$esc[48;2;133;133;133m$u$esc[38;2;48;118;168m$esc[48;2;251;251;252m$u$esc[38;2;252;252;252m$esc[48;2;248;248;248m$u$esc[38;2;254;254;255m$esc[48;2;254;254;254m$u$esc[38;2;255;255;255m$esc[48;2;255;255;255m$u$esc[38;2;249;248;248m$esc[48;2;246;246;245m$u$esc[38;2;139;141;141m$esc[48;2;72;74;74m$u$esc[38;2;90;71;64m$esc[48;2;79;52;40m$u$esc[38;2;61;31;21m$esc[48;2;101;41;25m$u$esc[38;2;14;69;71m$esc[48;2;17;82;81m$u$esc[38;2;59;65;89m$esc[48;2;55;61;83m$u$esc[38;2;27;70;118m$esc[48;2;22;44;84m$u$esc[38;2;8;19;33m$esc[48;2;2;4;6m$u$esc[0m $esc[0m   $esc[36m[*]$esc[0m $esc[1mSaved configurations:$esc[0m Kept safe in AppData
$esc[0m $esc[38;2;0;0;0m$esc[49m$u$esc[38;2;16;36;65m$esc[48;2;10;23;40m$u$esc[38;2;43;47;65m$esc[48;2;17;60;109m$u$esc[38;2;60;66;90m$esc[48;2;43;47;64m$u$esc[38;2;102;46;27m$esc[48;2;47;38;31m$u$esc[38;2;98;59;44m$esc[48;2;47;25;16m$u$esc[38;2;77;68;64m$esc[48;2;74;70;68m$u$esc[38;2;70;71;72m$esc[48;2;82;84;84m$u$esc[38;2;72;72;72m$esc[48;2;79;79;79m$u$esc[38;2;93;93;93m$esc[48;2;78;78;78m$u$esc[38;2;52;52;52m$esc[48;2;59;59;59m$u$esc[38;2;42;112;152m$esc[48;2;116;116;116m$u$esc[38;2;255;255;255m$esc[48;2;182;182;182m$u$esc[38;2;93;93;93m$esc[48;2;102;103;103m$u$esc[38;2;93;93;93m$esc[48;2;103;103;103m$u$esc[38;2;255;255;255m$esc[48;2;184;184;184m$u$esc[38;2;48;118;168m$esc[48;2;107;107;107m$u$esc[38;2;65;65;65m$esc[48;2;87;87;87m$u$esc[38;2;116;116;116m$esc[48;2;181;181;181m$u$esc[38;2;107;108;108m$esc[48;2;177;177;178m$u$esc[38;2;107;105;104m$esc[48;2;176;175;175m$u$esc[38;2;92;61;48m$esc[48;2;105;66;51m$u$esc[38;2;100;58;41m$esc[48;2;77;38;23m$u$esc[38;2;63;25;17m$esc[48;2;16;16;15m$u$esc[38;2;52;57;78m$esc[48;2;14;97;119m$u$esc[38;2;44;48;66m$esc[48;2;20;63;111m$u$esc[38;2;15;36;65m$esc[48;2;10;24;41m$u$esc[38;2;0;0;0m$esc[49m$u$esc[0m $esc[0m       $esc[90m(ready whenever you come back)$esc[0m
$esc[0m $esc[0m $esc[38;2;2;7;9m$esc[48;2;0;0;0m$u$esc[38;2;39;51;78m$esc[48;2;77;81;87m$u$esc[38;2;121;139;155m$esc[48;2;130;141;166m$u$esc[38;2;49;54;74m$esc[48;2;58;64;87m$u$esc[38;2;0;27;33m$esc[48;2;15;51;67m$u$esc[38;2;106;103;102m$esc[48;2;202;191;191m$u$esc[38;2;153;155;155m$esc[48;2;241;236;235m$u$esc[38;2;144;145;145m$esc[48;2;218;219;218m$u$esc[38;2;155;155;155m$esc[48;2;187;188;188m$u$esc[38;2;129;129;129m$esc[48;2;124;124;124m$u$esc[38;2;40;41;40m$esc[48;2;78;78;78m$u$esc[38;2;101;101;101m$esc[48;2;63;64;64m$u$esc[38;2;197;197;197m$esc[48;2;57;57;57m$u$esc[38;2;197;197;197m$esc[48;2;55;55;55m$u$esc[38;2;104;104;104m$esc[48;2;81;81;81m$u$esc[38;2;62;62;62m$esc[48;2;163;163;163m$u$esc[38;2;186;186;186m$esc[48;2;230;229;229m$u$esc[38;2;255;255;255m$esc[48;2;251;252;252m$u$esc[38;2;255;255;255m$esc[48;2;208;200;197m$u$esc[38;2;243;239;237m$esc[48;2;92;54;38m$u$esc[38;2;116;77;60m$esc[48;2;98;46;30m$u$esc[38;2;54;20;10m$esc[48;2;39;62;71m$u$esc[38;2;32;112;120m$esc[48;2;58;63;87m$u$esc[38;2;123;143;160m$esc[48;2;126;140;165m$u$esc[38;2;39;51;78m$esc[48;2;76;80;86m$u$esc[38;2;2;7;9m$esc[48;2;0;0;0m$u$esc[0m $esc[0m $esc[0m   
$esc[0m $esc[0m $esc[0m $esc[38;2;0;0;2m$esc[49m$u$esc[38;2;13;35;67m$esc[48;2;3;8;13m$u$esc[38;2;22;79;129m$esc[48;2;23;71;105m$u$esc[38;2;48;53;72m$esc[48;2;61;67;92m$u$esc[38;2;80;144;155m$esc[48;2;50;55;75m$u$esc[38;2;94;190;200m$esc[48;2;38;41;57m$u$esc[38;2;236;229;233m$esc[48;2;84;205;210m$u$esc[38;2;205;204;203m$esc[48;2;236;229;232m$u$esc[38;2;156;157;157m$esc[48;2;247;243;242m$u$esc[38;2;114;114;114m$esc[48;2;223;226;225m$u$esc[38;2;103;103;103m$esc[48;2;220;220;220m$u$esc[38;2;106;107;106m$esc[48;2;222;222;222m$u$esc[38;2;99;99;99m$esc[48;2;220;220;220m$u$esc[38;2;142;142;142m$esc[48;2;233;234;234m$u$esc[38;2;242;242;242m$esc[48;2;254;255;255m$u$esc[38;2;254;255;255m$esc[48;2;255;255;255m$u$esc[38;2;255;255;255m$esc[48;2;240;241;243m$u$esc[38;2;224;199;194m$esc[48;2;114;183;182m$u$esc[38;2;87;45;26m$esc[48;2;53;41;41m$u$esc[38;2;40;67;76m$esc[48;2;57;62;85m$u$esc[38;2;46;50;69m$esc[48;2;61;67;92m$u$esc[38;2;25;80;130m$esc[48;2;23;71;105m$u$esc[38;2;13;35;67m$esc[48;2;4;8;13m$u$esc[38;2;0;0;2m$esc[49m$u$esc[0m $esc[0m $esc[0m $esc[0m   $esc[90mCrafted with care by FoxDen Software$esc[0m
$esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[38;2;5;26;30m$esc[48;2;0;0;0m$u$esc[38;2;21;76;109m$esc[48;2;3;4;14m$u$esc[38;2;22;80;113m$esc[48;2;18;40;82m$u$esc[38;2;40;44;60m$esc[48;2;26;80;130m$u$esc[38;2;44;48;66m$esc[48;2;25;97;126m$u$esc[38;2;78;85;117m$esc[48;2;41;45;61m$u$esc[38;2;213;230;234m$esc[48;2;60;66;90m$u$esc[38;2;255;255;255m$esc[48;2;138;205;211m$u$esc[38;2;255;255;255m$esc[48;2;243;237;239m$u$esc[38;2;255;255;255m$esc[48;2;255;255;255m$u$esc[38;2;255;255;255m$esc[48;2;255;255;255m$u$esc[38;2;255;255;255m$esc[48;2;244;238;240m$u$esc[38;2;255;250;250m$esc[48;2;139;206;212m$u$esc[38;2;210;226;230m$esc[48;2;60;66;90m$u$esc[38;2;77;84;115m$esc[48;2;40;44;60m$u$esc[38;2;43;47;65m$esc[48;2;25;98;126m$u$esc[38;2;39;42;58m$esc[48;2;27;79;128m$u$esc[38;2;20;78;112m$esc[48;2;19;38;80m$u$esc[38;2;20;73;107m$esc[48;2;2;4;13m$u$esc[38;2;5;26;30m$esc[48;2;0;0;0m$u$esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[0m   
$esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[38;2;4;12;22m$esc[49m$u$esc[38;2;16;35;68m$esc[48;2;1;3;4m$u$esc[38;2;27;72;126m$esc[48;2;16;36;67m$u$esc[38;2;41;45;61m$esc[48;2;28;69;123m$u$esc[38;2;49;53;73m$esc[48;2;43;47;64m$u$esc[38;2;49;53;73m$esc[48;2;46;50;69m$u$esc[38;2;62;188;195m$esc[48;2;45;50;68m$u$esc[38;2;174;217;223m$esc[48;2;53;58;79m$u$esc[38;2;174;217;222m$esc[48;2;52;57;78m$u$esc[38;2;63;189;196m$esc[48;2;46;50;69m$u$esc[38;2;49;53;73m$esc[48;2;45;50;68m$u$esc[38;2;50;55;75m$esc[48;2;43;47;64m$u$esc[38;2;41;45;61m$esc[48;2;28;70;123m$u$esc[38;2;27;72;126m$esc[48;2;16;36;67m$u$esc[38;2;17;35;69m$esc[48;2;1;3;4m$u$esc[38;2;5;12;22m$esc[49m$u$esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[0m   
$esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[38;2;1;1;2m$esc[49m$u$esc[38;2;11;26;45m$esc[48;2;0;0;0m$u$esc[38;2;23;50;97m$esc[48;2;4;11;17m$u$esc[38;2;26;76;127m$esc[48;2;16;32;59m$u$esc[38;2;55;61;83m$esc[48;2;11;42;87m$u$esc[38;2;30;94;118m$esc[48;2;92;127;154m$u$esc[38;2;30;95;118m$esc[48;2;93;127;154m$u$esc[38;2;55;61;83m$esc[48;2;12;42;87m$u$esc[38;2;27;76;127m$esc[48;2;16;32;59m$u$esc[38;2;24;49;95m$esc[48;2;4;10;17m$u$esc[38;2;11;25;44m$esc[48;2;0;0;0m$u$esc[38;2;1;2;3m$esc[49m$u$esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[0m   
$esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[38;2;1;0;0m$esc[49m$u$esc[38;2;0;4;8m$esc[49m$u$esc[38;2;68;77;95m$esc[48;2;0;0;1m$u$esc[38;2;68;78;95m$esc[48;2;0;1;1m$u$esc[38;2;0;4;8m$esc[49m$u$esc[38;2;1;0;0m$esc[49m$u$esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[0m $esc[0m   
"@
Write-Host $banner

# 1. Administrator Elevation Check
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "`n[!] Administrator privileges are required to clean up scheduled tasks and Program Files." -ForegroundColor Yellow
    Write-Host "[*] Requesting elevation..." -ForegroundColor Cyan
    $extraArgs = ""
    if ($Force) { $extraArgs += " -Force" }
    if ($Silent) { $extraArgs += " -Silent" }
    if ($PSCommandPath -and (Test-Path $PSCommandPath)) {
        Start-Process powershell -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`"$extraArgs"
    } else {
        $tempScript = Join-Path $env:TEMP "WireFox_uninstall.ps1"
        $uninstallerUrl = "https://raw.githubusercontent.com/TalviFox/WireFox/main/uninstall.ps1"
        Invoke-WebRequest -Uri $uninstallerUrl -OutFile $tempScript -UseBasicParsing
        Start-Process powershell -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$tempScript`"$extraArgs"
    }
    return
}

# 2. Confirmation Prompt (safety gate before destructive removal)
if (-not ($Force -or $Silent)) {
    Write-Host ""
    Write-Host "[?] Are you sure you want to completely uninstall WireFox?" -ForegroundColor Yellow
    $confirm = Read-Host "    Type 'YES' to proceed with uninstall, or press Enter to cancel"
    if ($confirm -ne "YES") {
        Write-Host "`n[*] Uninstall cancelled. WireFox remains installed and untouched." -ForegroundColor Cyan
        Start-Sleep -Seconds 2
        return
    }
    Write-Host ""
}

# 3. Stop running WireFox process
Write-Host "[*] Terminating running WireFox processes..." -ForegroundColor Cyan
Get-Process -Name "WireFox" -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process -Name "WGManager" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800

# 4. Remove elevated Task Scheduler jobs
Write-Host "[*] Removing Task Scheduler background tasks..." -ForegroundColor Cyan
Unregister-ScheduledTask -TaskName "WireFox" -Confirm:$false -ErrorAction SilentlyContinue | Out-Null
Unregister-ScheduledTask -TaskName "WireGuardManager" -Confirm:$false -ErrorAction SilentlyContinue | Out-Null
schtasks.exe /Delete /TN "WireFox" /F *>$null
schtasks.exe /Delete /TN "WireGuardManager" /F *>$null

# 5. Remove Start Menu shortcuts
Write-Host "[*] Removing Start Menu shortcuts..." -ForegroundColor Cyan
$startMenuShortcut = "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\WireFox.lnk"
if (Test-Path $startMenuShortcut) {
    Remove-Item -Path $startMenuShortcut -Force -ErrorAction SilentlyContinue
}
$legacyShortcut = "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\WireGuardManager.lnk"
if (Test-Path $legacyShortcut) {
    Remove-Item -Path $legacyShortcut -Force -ErrorAction SilentlyContinue
}

# 6. Remove registry entries (Uninstall & Run keys)
Write-Host "[*] Cleaning registry entries..." -ForegroundColor Cyan
Remove-Item -Path "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\WireFox" -Recurse -Force -ErrorAction SilentlyContinue
Remove-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "WireFox" -Force -ErrorAction SilentlyContinue
Remove-ItemProperty -Path "HKLM:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "WireFox" -Force -ErrorAction SilentlyContinue
Remove-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "WireGuardManager" -Force -ErrorAction SilentlyContinue
Remove-ItemProperty -Path "HKLM:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "WireGuardManager" -Force -ErrorAction SilentlyContinue

# 7. Prompt user regarding saved configuration data
Write-Host ""
$localAppData = "$env:LocalAppData\WireFox"
$roamingAppData = "$env:AppData\WireFox"
$hasData = (Test-Path $localAppData) -or (Test-Path $roamingAppData)

if ($hasData) {
    Write-Host "`n[?] User Data & Saved Wi-Fi Networks:" -ForegroundColor Yellow
    Write-Host "    Your trusted networks and settings are currently kept safe in %AppData%." -ForegroundColor Gray
    $response = Read-Host "    Type 'DELETE' to permanently erase user settings, or press Enter to keep them safe"
    if ($response -eq "DELETE") {
        Remove-Item -Path $localAppData -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -Path $roamingAppData -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "[+] Removed configuration files from AppData." -ForegroundColor Green
    } else {
        Write-Host "[*] Saved configurations preserved in AppData (safe for future installs)." -ForegroundColor Cyan
    }
}

# 8. Remove Program Files directory
$installDir = "$env:ProgramFiles\WireFox"
if (Test-Path $installDir) {
    Write-Host "[*] Removing program files at $installDir..." -ForegroundColor Cyan
    Get-ChildItem -Path $installDir -Exclude "uninstall.ps1" | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    
    # Detach a quick background command to remove the folder once this script exits
    Start-Process cmd.exe -ArgumentList "/c timeout /t 1 /nobreak >nul & rmdir /s /q `"$installDir`"" -WindowStyle Hidden
}

$checkEmoji = [char]::ConvertFromUtf32(0x2705)
Write-Host @"

  =============================================================
     $checkEmoji WireFox has been cleanly and completely uninstalled.
     Thank you for using WireFox!
  =============================================================
"@ -ForegroundColor Green
Start-Sleep -Seconds 2
