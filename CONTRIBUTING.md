# Участие

Спасибо, что хотите улучшить плагин.

1. Клонируйте репозиторий вместе с субмодулем `QuickLook.Common`:

   ```bash
   git clone --recurse-submodules https://github.com/konditerov/quicklook-ocr.git
   ```

2. Соберите Release и прогоните проверки:

   ```powershell
   dotnet build QuickLook.Plugin.ImageViewerOcr.sln -c Release
   dotnet run --project QuickLook.Plugin.ImageViewerOcr.Tests -c Release --no-build
   powershell -File scripts/pack.ps1
   ```

3. Опишите в pull request, какое поведение меняется и как вы его проверили.

Сборка, кэш, личные проверочные картинки и служебные заметки в репозиторий не входят: они перечислены в `.gitignore`.

Плагин распространяется под GPL-3.0, потому что подключается к QuickLook. Модели PaddleOCR остаются под Apache License 2.0, см. `QuickLook.Plugin.ImageViewerOcr/models/NOTICE.txt`.
