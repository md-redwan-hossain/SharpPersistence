import { useEffect, useState } from "react";
import { Highlight, themes } from "prism-react-renderer";
import { Check, Copy, WrapText } from "lucide-react";
import { Button } from "./button";

function useIsDarkMode(): boolean {
  const [isDark, setIsDark] = useState(() =>
    typeof document !== "undefined" && document.documentElement.classList.contains("dark")
  );

  useEffect(() => {
    const root = document.documentElement;
    const update = () => setIsDark(root.classList.contains("dark"));
    update();
    const observer = new MutationObserver(update);
    observer.observe(root, { attributes: true, attributeFilter: ["class"] });
    return () => observer.disconnect();
  }, []);

  return isDark;
}

interface SqlCodeBlockProps {
  code: string;
  showToolbar?: boolean;
  maxHeightClass?: string;
}

export function SqlCodeBlock({
  code,
  showToolbar = true,
  maxHeightClass,
}: SqlCodeBlockProps) {
  const isDark = useIsDarkMode();
  const [wrap, setWrap] = useState(false);
  const [copied, setCopied] = useState(false);

  const trimmed = code.replace(/\r\n/g, "\n").trimEnd();
  const lineCount = trimmed ? trimmed.split("\n").length : 0;

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(code);
      setCopied(true);
      setTimeout(() => setCopied(false), 1600);
    } catch {
      // Clipboard unavailable
    }
  };

  return (
    <div className="flex flex-col bg-muted/30 dark:bg-zinc-950">
      {showToolbar && (
        <div className="px-3 sm:px-4 py-1.5 border-b border-border flex items-center justify-between gap-2 bg-muted/40">
          <span className="text-[11px] font-mono text-muted-foreground tabular-nums">
            SQL · {lineCount} {lineCount === 1 ? "line" : "lines"}
          </span>
          <div className="flex items-center gap-1.5">
            <Button
              variant={wrap ? "secondary" : "ghost"}
              size="sm"
              onClick={() => setWrap((w) => !w)}
              title="Toggle line wrap"
              className="h-7 px-2 text-[11px]"
            >
              <WrapText className="w-3.5 h-3.5" />
              Wrap
            </Button>
            <Button
              variant="outline"
              size="sm"
              onClick={() => void handleCopy()}
              className="h-7 px-2 text-[11px]"
            >
              {copied ? <Check className="w-3.5 h-3.5 text-emerald-500" /> : <Copy className="w-3.5 h-3.5" />}
              {copied ? "Copied" : "Copy SQL"}
            </Button>
          </div>
        </div>
      )}

      <div className={`overflow-auto ${maxHeightClass ?? ""}`}>
        <Highlight theme={isDark ? themes.oneDark : themes.github} code={trimmed} language="sql">
          {({ tokens, getLineProps, getTokenProps }) => (
            <pre className="m-0 py-3 px-2 font-mono text-xs leading-relaxed bg-transparent">
              {tokens.map((line, i) => {
                const lineProps = getLineProps({ line });
                return (
                  <div
                    key={i}
                    {...lineProps}
                    className={`${lineProps.className ?? ""} flex hover:bg-muted/50 dark:hover:bg-zinc-900/60 rounded-xs px-1`}
                  >
                    <span className="select-none w-10 pr-3 text-right text-muted-foreground/50 shrink-0 tabular-nums">
                      {i + 1}
                    </span>
                    <span className={wrap ? "whitespace-pre-wrap break-words min-w-0 flex-1" : "whitespace-pre"}>
                      {line.map((token, key) => (
                        <span key={key} {...getTokenProps({ token })} />
                      ))}
                    </span>
                  </div>
                );
              })}
            </pre>
          )}
        </Highlight>
      </div>
    </div>
  );
}
