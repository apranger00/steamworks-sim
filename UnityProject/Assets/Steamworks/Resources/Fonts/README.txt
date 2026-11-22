Game fonts (loaded by name, the file extension doesn't matter: .ttf or .otf)

  Title         menu and heading font. Put your licensed Brim Narrow or Haymaker file here,
                renamed to Title.ttf / Title.otf. While it is missing, TitleDefault is used.
  TitleDefault  Anton (SIL Open Font License, OFL-Anton.txt)
  Body          Fira Sans Regular (SIL Open Font License, OFL-FiraSans.txt)
  BodyBold      Fira Sans SemiBold

Title.ttf / Title.otf are ignored by git (.gitignore): personal-use font licences (Haymaker's included)
forbid giving the font file to anyone else, and this repository is public. Keep your copy here locally.
A built game that you share with others also contains the font, so for that you need a licence that
allows app/game embedding, or remove Title.* and the game falls back to Anton.

Brim Narrow is a layered font family: use a single solid style (e.g. "Face") or Brim Combined.
