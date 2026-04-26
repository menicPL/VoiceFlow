# 🎤 VoiceFlow AI v1.0: Instrukcja Instalacji (PL)

Aplikacja VoiceFlow AI (wersja C#/.NET 8) jest gotowa do użycia. Możesz ją uruchomić bezpośrednio plikiem **VoiceFlowCS.exe**.

## 1. Konfiguracja Google Cloud (STT)

Aby aplikacja mogła rozpoznawać Twój głos (Real-time Speech-to-Text), potrzebujesz klucza JSON:

1. Wejdź na [Google Cloud Console](https://console.cloud.google.com/).
2. Utwórz projekt i włącz **"Cloud Speech-to-Text API"**.
3. Utwórz **"Konto usługi"** (Service Account) w zakładce "Dane uwierzytelniające".
4. Dodaj klucz w formacie **JSON**, pobierz go i nazwij `google_creds.json`.
5. Umieść plik w głównym folderze aplikacji (obok .exe) lub wskaż jego ścieżkę w **Ustawieniach** aplikacji.

## 2. Klucz Gemini AI (Dla odpowiedzi AI)

Aplikacja wykorzystuje model Gemini do generowania odpowiedzi:

1. Pobierz darmowy klucz API z [Google AI Studio](https://aistudio.google.com/).
2. Kliknij **"Get API key"**.
3. Skopiuj klucz i wklej go w aplikacji w menu **Ustawienia** (ikona ⚙️) lub utwórz plik `.env`:
   `GEMINI_API_KEY=TWOJ_KLUCZ_TUTAJ`

## 3. Co nowego w v1.0?

*   **Trwałość (Persistence)**: Aplikacja zapamiętuje wybrane urządzenie, język, klucze API oraz historię czatu.
*   **Menu Ustawień (Settings)**: Możesz teraz łatwo konfigurować klucze, zmieniać wielkość czcionki oraz wybierać model Gemini (np. Flash lub Pro).
*   **Wielojęzyczność**: Przełącznik PL/EN poprawia celność rozpoznawania mowy w zależności od kontekstu.
*   **Manual Query**: Możesz wpisywać pytania ręcznie w dolnym polu tekstowym.

## 4. Rozwiązywanie problemów:

- **Nie słyszy?** Wybierz poprawne urządzenie "Input". Jeśli chcesz słuchać dźwięku z komputera (np. ze spotkania Teams), wybierz urządzenie oznaczone jako `[LOOPBACK]`.
- **Błąd API?** Sprawdź w Ustawieniach czy klucze są poprawne i czy plik JSON istnieje w podanej ścieżce.
- **Interfejs**: Aplikacja jest "Always on Top" (zawsze na wierzchu), co można wyłączyć w ustawieniach.

---
*Projekt rozwijany w C# (WPF .NET 8).*
