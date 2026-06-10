// A combobox: a text input backed by a native <datalist>, so the Tester can type to filter the
// options OR open the dropdown and pick. Free text is allowed (degrades gracefully for values not
// in the list). Native datalist = no JS filtering, works offline, accessible.
interface Props {
  value?: string | null;
  onChange: (value: string | null) => void;
  options: readonly string[];
  /** Unique id linking the input to its <datalist>. */
  listId: string;
  placeholder?: string;
  class?: string;
}

export function Combobox({ value, onChange, options, listId, placeholder, class: className }: Props) {
  return (
    <>
      <input
        class={className}
        list={listId}
        value={value ?? ""}
        placeholder={placeholder ?? "Type or select…"}
        onInput={(e) => {
          const v = (e.currentTarget as HTMLInputElement).value;
          onChange(v.trim() === "" ? null : v);
        }}
      />
      <datalist id={listId}>
        {options.map((o) => (
          <option key={o} value={o} />
        ))}
      </datalist>
    </>
  );
}
