// Reads one readable line from an API error body. The server's own text is shown when it has one: a validation
// list (ProblemDetails with "errors"), or the title or detail of a ProblemDetails object, or a plain string.
// A generic server error also carries its correlation id, so the failure can be found in the server logs.
export function apiErrorMessage(body: unknown, fallback: string): string {
  if (body instanceof Error && body.message.trim()) {
    return body.message.trim();
  }

  if (typeof body === 'string' && body.trim()) {
    return body.trim();
  }

  if (!body || typeof body !== 'object') {
    return fallback;
  }

  const record = body as Record<string, unknown>;
  const validationMessages = validationTexts(record.errors);
  if (validationMessages.length > 0) {
    return validationMessages.join(' ');
  }

  const message = firstText(record, ['detail', 'message', 'title', 'error']);
  if (!message) {
    return fallback;
  }

  const correlationId = firstText(record, ['correlationId']);
  return correlationId ? `${message} (reference ${correlationId})` : message;
}

function validationTexts(errors: unknown): string[] {
  if (errors && typeof errors === 'object' && !Array.isArray(errors)) {
    // ProblemDetails from the server groups messages by field: { "CurrentPassword": ["..."] }.
    const grouped: string[] = [];
    for (const messages of Object.values(errors as Record<string, unknown>)) {
      if (!Array.isArray(messages)) {
        continue;
      }

      for (const message of messages) {
        if (typeof message === 'string' && message.trim()) {
          grouped.push(message.trim());
        }
      }
    }

    return grouped;
  }

  if (!Array.isArray(errors)) {
    return [];
  }

  const texts: string[] = [];
  for (const item of errors) {
    if (!item || typeof item !== 'object') {
      continue;
    }

    const entry = item as Record<string, unknown>;
    const text = firstText(entry, ['errorMessage', 'ErrorMessage']);
    if (text) {
      texts.push(text);
    }
  }

  return texts;
}

function firstText(record: Record<string, unknown>, keys: string[]): string | null {
  for (const key of keys) {
    const value = record[key];
    if (typeof value === 'string' && value.trim()) {
      return value.trim();
    }
  }

  return null;
}
