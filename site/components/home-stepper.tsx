'use client';

import { Fragment, useEffect, useId, useRef, useState } from 'react';
import type { KeyboardEvent as ReactKeyboardEvent, ReactNode } from 'react';

/**
 * One step in the home page stepper: the example file, its pre-rendered highlighted HTML, and
 * the step title and text shown in the step list.
 */
export type HomeStepperFile = Readonly<{
  /** File name shown on the tab and under the step text, for example `Program.cs`. */
  name: string;
  /** Breadcrumb segments shown above the code, for example `src`, `Program.cs`. */
  crumbs: readonly string[];
  /** Highlighted file HTML from `highlightToHtml`: a `pre.code` with one `.line` per line. */
  html: string;
  /** Step title shown in the step list, for example `Register LiteBus`. */
  title: string;
  /** Step text shown under the title; may hold inline elements such as `code`. */
  text: ReactNode;
}>;

/**
 * Getting-started stepper: three steps driving one fixed-size editor.
 *
 * The step list and the editor tabs select the same file. The panel remounts on every selection
 * (through `key`) so the rewrite animation restarts, and an effect wraps each `.line` content in
 * a `.lc` span carrying the `--i` stagger index, keeping the `::before` line number visible
 * while the code is written. Tabs follow the tablist keyboard pattern: arrows move between
 * files, Home and End jump to the first and last file.
 */
export function HomeStepper({ files }: Readonly<{ files: readonly HomeStepperFile[] }>) {
  const [selected, setSelected] = useState(0);
  const baseId = useId();
  const tabRefs = useRef<Array<HTMLButtonElement | null>>([]);
  const codeHostRef = useRef<HTMLDivElement>(null);
  const file = files[selected];
  const panelId = `${baseId}-panel`;

  /** Tab element id for the file at the given index. */
  function tabId(index: number): string {
    return `${baseId}-tab-${index}`;
  }

  useEffect(() => {
    const host = codeHostRef.current;
    if (host === null) {
      return;
    }
    host.querySelectorAll('.line').forEach((line, index) => {
      const element = line as HTMLElement;
      if (element.querySelector(':scope > .lc') !== null) {
        return;
      }
      element.style.setProperty('--i', String(index));
      const wrap = document.createElement('span');
      wrap.className = 'lc';
      while (line.firstChild !== null) {
        wrap.appendChild(line.firstChild);
      }
      line.appendChild(wrap);
    });
  }, [selected]);

  /** Moves selection and focus across tabs with arrows, Home, and End. */
  function onTabsKeyDown(event: ReactKeyboardEvent<HTMLDivElement>): void {
    let next: number | null = null;
    if (event.key === 'ArrowRight' || event.key === 'ArrowDown') {
      next = (selected + 1) % files.length;
    } else if (event.key === 'ArrowLeft' || event.key === 'ArrowUp') {
      next = (selected + files.length - 1) % files.length;
    } else if (event.key === 'Home') {
      next = 0;
    } else if (event.key === 'End') {
      next = files.length - 1;
    }
    if (next === null) {
      return;
    }
    event.preventDefault();
    setSelected(next);
    tabRefs.current[next]?.focus();
  }

  return (
    <div className="stepper">
      <div className="steplist" role="group" aria-label="Steps">
        {files.map((step, index) => (
          <button
            key={step.name}
            type="button"
            className={index === selected ? 'step on' : 'step'}
            aria-pressed={index === selected}
            onClick={() => setSelected(index)}
          >
            <span className="num" aria-hidden="true">
              {index + 1}
            </span>
            <b>{step.title}</b>
            <span className="d">{step.text}</span>
            <span className="fname">{step.name}</span>
          </button>
        ))}
      </div>
      <div className="ed">
        <div className="ed-tabs" role="tablist" aria-label="Example files" onKeyDown={onTabsKeyDown}>
          {files.map((step, index) => (
            <button
              key={step.name}
              ref={(element) => {
                tabRefs.current[index] = element;
              }}
              type="button"
              role="tab"
              id={tabId(index)}
              aria-selected={index === selected}
              aria-controls={panelId}
              tabIndex={index === selected ? 0 : -1}
              className={index === selected ? 'ed-tab on' : 'ed-tab'}
              onClick={() => setSelected(index)}
            >
              {step.name}
              <span className="x" aria-hidden="true">
                x
              </span>
            </button>
          ))}
        </div>
        <div
          key={selected}
          className="file on write"
          role="tabpanel"
          id={panelId}
          aria-labelledby={tabId(selected)}
          tabIndex={0}
        >
          <div className="crumbs">
            {file.crumbs.map((crumb, index) => (
              <Fragment key={`${index}-${crumb}`}>
                {index > 0 && (
                  <b aria-hidden="true">{'>'}</b>
                )}
                {crumb}
              </Fragment>
            ))}
          </div>
          <div ref={codeHostRef} dangerouslySetInnerHTML={{ __html: file.html }} />
        </div>
        <div className="ed-foot" aria-hidden="true">
          <span>C#</span>
          <span>UTF-8</span>
          <span>LF</span>
        </div>
      </div>
    </div>
  );
}
