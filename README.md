# MechatrolinkParser

A parser for Mechatrolink-I/II commucation snapshots.

Done: essential stuff, i.e. text import, Manchester decoder, HDLC parser and data structures, also some basic commandline interface. Command and field database engine, FCS calculator (uses HashFunction.CRC NuGet package).

Todo: visualisation (probably use my LogicSnifferSTM plotter), improve command database.

Supported logic analyzer export formats: Kingst .txt (similar to CSV), LogicSnifferSTM (colon-separated).

Output example:

![new example](https://i.imgur.com/4wn0BKe.png)
