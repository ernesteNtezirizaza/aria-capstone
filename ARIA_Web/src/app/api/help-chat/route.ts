import { NextRequest, NextResponse } from "next/server";
import { SYSTEM_PROMPT } from "@/lib/help-chat-knowledge";

const GEMINI_MODEL = "gemini-2.5-flash";

type ChatMessage = {
  role: "user" | "assistant";
  content: string;
};

export async function POST(req: NextRequest) {
  const apiKey = process.env.GEMINI_API_KEY;
  if (!apiKey) {
    return NextResponse.json(
      { error: "Chatbot is not configured (missing GEMINI_API_KEY)." },
      { status: 500 }
    );
  }

  let messages: ChatMessage[];
  try {
    const body = await req.json();
    messages = body.messages;
    if (!Array.isArray(messages) || messages.length === 0) {
      throw new Error("messages must be a non-empty array");
    }
  } catch {
    return NextResponse.json({ error: "Invalid request body." }, { status: 400 });
  }

  // Cap history sent upstream -- a help widget doesn't need unbounded context.
  const recent = messages.slice(-20);
  const contents = recent.map((m) => ({
    role: m.role === "assistant" ? "model" : "user",
    parts: [{ text: m.content }],
  }));

  const url = `https://generativelanguage.googleapis.com/v1beta/models/${GEMINI_MODEL}:generateContent?key=${apiKey}`;

  let geminiRes: Response;
  try {
    geminiRes = await fetch(url, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        system_instruction: { parts: [{ text: SYSTEM_PROMPT }] },
        contents,
        generationConfig: { temperature: 0.4, maxOutputTokens: 500 },
      }),
    });
  } catch {
    return NextResponse.json(
      { error: "Could not reach the chatbot service. Please try again." },
      { status: 502 }
    );
  }

  if (!geminiRes.ok) {
    const errText = await geminiRes.text().catch(() => "");
    console.error("[help-chat] Gemini API error", geminiRes.status, errText);
    return NextResponse.json(
      { error: "The chatbot service returned an error. Please try again." },
      { status: 502 }
    );
  }

  const data = await geminiRes.json();
  const reply: string | undefined =
    data?.candidates?.[0]?.content?.parts?.map((p: { text?: string }) => p.text ?? "").join("") ;

  if (!reply) {
    const blockReason = data?.promptFeedback?.blockReason;
    return NextResponse.json(
      {
        reply: blockReason
          ? "I can't answer that one. Could you rephrase your question about ARIA?"
          : "I didn't get a response that time -- could you try asking again?",
      },
      { status: 200 }
    );
  }

  return NextResponse.json({ reply });
}
