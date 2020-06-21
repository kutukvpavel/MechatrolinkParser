# MechatrolinkParser

A parser for Mechatrolink-I/II commucation snapshots.

Done: essential stuff, i.e. text import, Manchester decoder, HDLC parser and data structures, also some basic commandline interface. Command and field database engine, FCS calculator (uses HashFunction.CRC NuGet package).

Todo: visualisation (probably use my LogicSnifferSTM plotter), improve command database. **P.S. Full Mechatrolink-II system manual is exclusively available on CSDN.net (costs 22 C-points, though I don't know what they even are). Help appreciated.**

Supported logic analyzer export formats: Kingst .txt (similar to CSV), LogicSnifferSTM (colon-separated). Logic analyzer has to be connected through a receiver assembly to the mechatrolink bus, see https://electronics.stackexchange.com/questions/451498/reverse-engineering-rs485-mechatrolink-ii-front-end-design . In short, a 1:1 350uH transformer + АМ26LS32-compatible RS485 receiver.

Command/field database was created using generic manuals (search "MECHATROLINK-I-II" on Baidu and look for Command Manuals ("for stepper motors" etc)). Therefore, beware that your hardware may use slightly different commandset.

Output example:

![new example](https://i.imgur.com/4wn0BKe.png)
