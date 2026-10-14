# QuickLook.Plugin.ImageViewerOcr

[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](LICENSE)
[![GitHub stars](https://img.shields.io/github/stars/konditerov/quicklook-ocr?style=social)](https://github.com/konditerov/quicklook-ocr/stargazers)

Плагин для [QuickLook](https://github.com/QL-Win/QuickLook): картинка открывается сразу, текст на ней можно выделить и скопировать. Распознавание идёт на компьютере, без отправки изображения в сеть.

Если плагин экономит вам время, поставьте [звезду](https://github.com/konditerov/quicklook-ocr/stargazers). По ней репозиторий проще найти тем, кому тоже нужен текст прямо с фото и скриншотов.

**English.** A QuickLook plugin that shows PNG, JPEG, BMP and TIFF immediately, then lets you select and copy the text. The default engine is offline PaddleOCR (Russian, Ukrainian, Belarusian, English and digits), with Windows OCR as a fallback. Preview the `.qlplugin` with Space, click Install, and restart QuickLook. Stars help other people find it.

| | |
| --- | --- |
| Автор | [Ilia Konditerov](https://github.com/konditerov) |
| Дата | 15 октября 2026 |
| Версия | 0.1.0 |
| Лицензия | [GPL-3.0](LICENSE) |

Запрос на эту возможность в QuickLook: [#1608](https://github.com/QL-Win/QuickLook/issues/1608). Мейнтейнер оставил распознавание сторонним плагинам.

## Установка

Нужны 64-битная Windows 10 или 11 и запущенный QuickLook.

1. Скачайте `QuickLook.Plugin.ImageViewerOcr.qlplugin` со [страницы релиза](https://github.com/konditerov/quicklook-ocr/releases/latest). В архиве около 26 МБ: сборка плагина, ONNX Runtime для win-x64 и мобильные модели.
2. Выделите скачанный файл и нажмите пробел. В окне QuickLook нажмите **Install**.
3. Перезапустите QuickLook.
4. Откройте пробелом PNG, JPEG, BMP или TIFF. Картинка появляется сразу, текст становится доступен, когда распознавание закончится.

Повторная установка заменяет каталог `%AppData%\pooi.moe\QuickLook\QuickLook.Plugin\QuickLook.Plugin.ImageViewerOcr\`. У переносной сборки QuickLook с файлом `portable.lock` тот же каталог лежит в `UserData\QuickLook.Plugin\` рядом с программой.

## Как пользоваться

- Протяните мышь по тексту. Двойной щелчок выделяет слово.
- `Ctrl+C` копирует выделение. Если выделения нет, копируется весь распознанный текст.
- Правый щелчок: **Копировать** и **Копировать весь текст**.
- Колёсико меняет масштаб от курсора. Средняя кнопка мыши сдвигает картинку. Пока масштаб не трогали, картинка вписывается в окно.
- Над текстом курсор становится текстовым.
- Пункт **Настройки распознавания текста** в меню дополнительных действий окна предпросмотра открывает настройки плагина.

GIF, APNG и SVG по-прежнему открывает встроенный просмотрщик QuickLook: у этого плагина приоритет выше только для неподвижных растров `.png`, `.jpg`, `.jpeg`, `.bmp`, `.tif`, `.tiff`.

## Что реализовано

Окно предпросмотра рисует само изображение и прозрачный слой слов в одном масштабе. Слой двигается и увеличивается вместе с картинкой, поэтому подсветка остаётся на буквах.

Распознавание стартует в фоне после показа файла и отменяется, если окно закрыли или открыли другую картинку. При старте QuickLook модели заранее прогреваются, чтобы первый скриншот не ждал загрузки ONNX.

Результат кэшируется в `%LocalAppData%\QuickLook.Plugin.ImageViewerOcr\cache`. Ключ кэша — путь, размер, время изменения файла и идентификатор движка. Повторное открытие того же файла читает готовый JSON.

Если модель отсутствует или движок завершился ошибкой, картинка всё равно остаётся на экране.

### Движки

В режиме **Авто** плагин идёт по списку и берёт первый доступный движок, который вернул страницу:

1. **PaddleOCR.** Основной путь. Локальные модели PP-OCRv5 mobile, экспорт [RapidOCR](https://github.com/RapidAI/RapidOCR) с PaddleOCR:
   - `det.onnx` — `ch_PP-OCRv5_det_mobile`, поиск областей текста;
   - `rec_eslav.onnx` и `dict_eslav.txt` — `eslav_PP-OCRv5_rec_mobile`, русский, украинский, белорусский, английский и цифры.
2. **Windows AI.** Пункт в настройках зарезервирован под [Windows AI Text Recognition](https://learn.microsoft.com/en-us/windows/ai/apis/text-recognition). Вызов распознавания в этой версии не подключён. В режиме **Авто** плагин переходит к следующему движку.
3. **Windows OCR** (`Windows.Media.Ocr`). Запасной движок, когда установлен языковой пакет. Галочки «Русский» и «Английский» относятся только к нему.

Сессия ONNX открывается через DirectML. Если видеоадаптер её не поднимает, та же модель считается на процессоре.

Длинная сторона кадра для детектора ограничена 960 пикселями. Координаты слов пересчитываются обратно в размер исходного файла, в том числе когда снимок больше 4096 пикселей по стороне. Для показа такой снимок декодируется с длинной стороной 4096.

Строка режется на слова по пробелам, рамка каждого слова сжимается по чернилам. Выделение хранится как диапазон символов и рисуется прямоугольниками поверх слоя.

## Стек

- C#, .NET Framework 4.7.2, WPF
- Контракт плагина QuickLook: `IViewer` и `IMoreMenu`, субмодуль [QuickLook.Common](https://github.com/QL-Win/QuickLook.Common)
- Microsoft.ML.OnnxRuntime.DirectML 1.19.2
- System.Text.Json 8
- Windows Runtime: `Windows.Media.Ocr`

## Сборка из исходников

Нужны Windows, .NET SDK 8 или новее и средства сборки .NET Framework 4.7.2 (их ставит Visual Studio или соответствующий workload).

```powershell
git clone --recurse-submodules https://github.com/konditerov/quicklook-ocr.git
cd quicklook-ocr
dotnet build QuickLook.Plugin.ImageViewerOcr.sln -c Release
powershell -File scripts/pack.ps1
```

Готовый пакет: `dist\QuickLook.Plugin.ImageViewerOcr.qlplugin`.

Проверки геометрии, кэша и распознавания на синтетических картинках:

```powershell
dotnet run --project QuickLook.Plugin.ImageViewerOcr.Tests -c Release --no-build
```

Каталоги сборки, `dist`, личные проверочные снимки и служебные заметки перечислены в `.gitignore` и в GitHub не попадают.

## Лицензия

Код плагина — [GNU GPL 3.0](LICENSE), потому что он связывается с QuickLook. Модели PaddleOCR распространяются под Apache License 2.0; подробности в [`models/NOTICE.txt`](QuickLook.Plugin.ImageViewerOcr/models/NOTICE.txt).
