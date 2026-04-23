# 🎤 VoiceFlow: Instrukcja Instalacji (PL)

Aplikacja jest gotowa. Możesz ją uruchomić bezpośrednio plikiem **VoiceFlow.exe**. Aby jednak działała transkrypcja, potrzebujesz klucza od Google Cloud (polskie menu).

## 1. Uzyskanie klucza Google Cloud (po polsku)

1. Wejdź na [Google Cloud Console](https://console.cloud.google.com/).
2. Na górnym pasku wybierz lub utwórz nowy **Projekt**.
3. W menu po lewej wybierz **"Interfejsy API i usługi"** -> **"Biblioteka"**.
4. Wyszukaj i włącz: **"Cloud Speech-to-Text API"**.
5. Wróć do menu i wybierz **"Interfejsy API i usługi"** -> **"Dane uwierzytelniające"**.
6. Kliknij na górze **"+ UTWÓRZ DANE UWIERZYTELNIAJĄCE"** i wybierz **"Konto usługi"**.
7. Po utworzeniu konta, kliknij w jego adres e-mail na liście.
8. Przejdź do zakładki **"Klucze"** (na górze).
9. Kliknij **"Dodaj klucz"** -> **"Utwórz nowy klucz"** -> wybierz format **JSON**.
10. Pobrany plik nazwij `google_creds.json` i wrzuć go do folderu z aplikacją VoiceFlow.

## 2. Klucz Gemini AI (Dla odpowiedzi AI)

Aplikacja teraz nie tylko spisuje tekst, ale też na niego odpowiada! Potrzebujesz do tego darmowego klucza Gemini:
1. Wejdź na [Google AI Studio](https://aistudio.google.com/).
2. Kliknij **"Get API key"**.
3. Skopiuj klucz i wklej go do pliku `.env` w folderze aplikacji:
   `GEMINI_API_KEY=TWOJ_KLUCZ_TUTAJ`

## 3. Uruchomienie

1. Upewnij się, że pliki `google_creds.json` oraz `.env` są w folderze.
2. Odpal **VoiceFlow.exe** (lub `python main.py`).
3. Zobaczysz pasek głośności (**Loudness Bar**) - to znak, że aplikacja słucha!
4. Po zakończeniu Twojej wypowiedzi, Gemini automatycznie odpowie.

### Rozwiązywanie problemów:
- **Brak odpowiedzi AI?** Sprawdź czy klucz w `.env` jest poprawny.
- **Pasek głośności się nie rusza?** Upewnij się, że wybrałeś poprawne urządzenie wejściowe ("Device").
- **Błąd biblioteki?** Jeśli uruchamiasz wersję skryptową, zainstaluj zależności: `pip install -r requirements.txt`.
