const metadata = JSON.parse(document.getElementById('metadata').textContent);
const q = (root, selector) => root.querySelector(selector);
const children = (root, selector) => Array.from(root.querySelectorAll(selector));
const fields = new Map(metadata.Fields.map((f) => [f.Name, f]));
function refresh(node, reset = false) {
  if (node.dataset.kind !== 'leaf') return;
  const field = fields.get(q(node, '.field').value),
    operator = q(node, '.operator');
  if (reset) operator.replaceChildren(...field.Operators.map((op) => new Option(op, op)));
  const type = field.Type;
  const input = q(node, '.literal');
  input.type =
    operator.value !== 'in' &&
    ['Int32', 'Int64', 'Decimal', 'Double', 'Single', 'Byte', 'Int16'].includes(type)
      ? 'number'
      : 'text';
  input.step = 'any';
  input.placeholder =
    operator.value === 'in'
      ? 'JSON array of typed values'
      : type === 'Boolean'
        ? 'true or false'
        : type;
  q(node, '.null').disabled = !field.Nullable;
  if (!field.Nullable) q(node, '.null').checked = false;
  const noValue = ['isNull', 'isNotNull'].includes(operator.value);
  q(node, '.literal-label').hidden = noValue || q(node, '.source').value !== 'literal';
  q(node, '.context-label').hidden = noValue || q(node, '.source').value !== 'context';
}
function leaf() {
  const template = document
    .getElementById('rule-template')
    .content.querySelector('.node[data-kind=leaf]')
    .cloneNode(true);
  refresh(template, true);
  return template;
}
function group(kind) {
  const node = document.createElement('fieldset');
  node.className = 'node';
  node.dataset.kind = kind;
  node.innerHTML = `<legend>${kind}</legend><div class="children"></div>${kind === 'not' ? '' : '<button type="button" class="add-leaf">Add leaf</button><button type="button" class="add-all">Add all</button><button type="button" class="add-any">Add any</button><button type="button" class="add-not">Add not</button>'}<button type="button" class="remove">Remove</button>`;
  q(node, '.children').append(leaf());
  return node;
}
function condition(node) {
  const kind = node.dataset.kind;
  if (kind === 'true') return null;
  if (kind === 'not') {
    const nested = q(node, '.children').children;
    if (nested.length !== 1) throw new Error('Not requires exactly one child.');
    return '{"not":' + condition(nested[0]) + '}';
  }
  if (kind === 'all' || kind === 'any') {
    const nested = Array.from(q(node, '.children').children);
    if (!nested.length) throw new Error('Groups require at least one condition.');
    return '{' + JSON.stringify(kind) + ':[' + nested.map(condition).join(',') + ']}';
  }
  const name = q(node, '.field').value,
    op = q(node, '.operator').value;
  const properties = '"field":' + JSON.stringify(name) + ',"operator":' + JSON.stringify(op);
  if (['isNull', 'isNotNull'].includes(op)) return '{' + properties + '}';
  if (q(node, '.source').value === 'context')
    return '{' + properties + ',"context":' + JSON.stringify(q(node, '.context-key').value) + '}';
  const raw = q(node, '.literal').value,
    type = fields.get(name).Type;
  let literal;
  if (q(node, '.null').checked) literal = 'null';
  else if (op === 'in') {
    JSON.parse(raw);
    literal = raw;
  } else if (['Int32', 'Int64', 'Decimal', 'Double', 'Single', 'Byte', 'Int16'].includes(type)) {
    if (!/^-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?$/.test(raw))
      throw new Error('Enter a valid numeric value.');
    literal = raw;
  } else if (type === 'Boolean') {
    if (!['true', 'false'].includes(raw)) throw new Error('Enter true or false.');
    literal = raw;
  } else literal = JSON.stringify(raw);
  return '{' + properties + ',"literal":' + literal + '}';
}
function subject(entry) {
  const value = {
    roles: Array.from(q(entry, '.roles').selectedOptions).map((o) => o.value),
    groups: q(entry, '.groups')
      .value.split(',')
      .map((v) => v.trim())
      .filter(Boolean),
  };
  const retained = q(entry, '.subject-condition');
  if (retained?.value) value.contextCondition = JSON.parse(retained.value);
  return value;
}
function policy() {
  const rules = Array.from(document.getElementById('rules').children).map((entry) => {
    const base = {
      id: q(entry, '.entry-id').value,
      resource: 'Employee',
      action: 'Read',
      effect: q(entry, '.effect').value,
      subject: subject(entry),
    };
    const node = condition(q(entry, '.node'));
    const json = JSON.stringify(base);
    return node ? json.slice(0, -1) + ',"condition":' + node + '}' : json;
  });
  const entries = Array.from(document.getElementById('entries').children).map((entry) => {
    const grants = {};
    children(entry, '.grant:checked').forEach((input) =>
      (grants[input.dataset.operation] ??= []).push(input.value),
    );
    return {
      id: q(entry, '.entry-id').value,
      resource: 'Employee',
      action: 'Read',
      effect: q(entry, '.effect').value,
      subject: subject(entry),
      fields: grants,
    };
  });
  return (
    '{"resource":"Employee","schemaVersion":"1","rules":[' +
    rules.join(',') +
    '],"fieldEntries":' +
    JSON.stringify(entries) +
    '}'
  );
}
document.addEventListener('change', (event) => {
  const node = event.target.closest('.node');
  if (node) refresh(node, event.target.matches('.field'));
});
document.addEventListener('click', (event) => {
  const button = event.target.closest('button');
  if (!button) return;
  if (button.classList.contains('remove-entry')) button.closest('section').remove();
  if (button.classList.contains('remove')) {
    const node = button.closest('.node');
    if (node.parentElement.classList.contains('record-rule')) {
      const unconditional = document.createElement('fieldset');
      unconditional.className = 'node';
      unconditional.dataset.kind = 'true';
      unconditional.innerHTML =
        '<legend>Unconditional</legend><button type="button" class="replace">Add condition</button>';
      node.replaceWith(unconditional);
    } else node.remove();
  }
  if (button.classList.contains('replace')) button.closest('.node').replaceWith(group('all'));
  for (const kind of ['leaf', 'all', 'any', 'not'])
    if (button.classList.contains('add-' + kind))
      q(button.closest('.node'), '.children').append(kind === 'leaf' ? leaf() : group(kind));
  if (button.id === 'add-rule' || button.id === 'add-entry') {
    const template = document
      .getElementById(button.id === 'add-rule' ? 'rule-template' : 'field-template')
      .content.cloneNode(true);
    q(template, '.entry-id').value = 'entry-' + crypto.randomUUID();
    document.getElementById(button.id === 'add-rule' ? 'rules' : 'entries').append(template);
  }
});
document.querySelectorAll('.node[data-kind=leaf]').forEach((n) => refresh(n));
document.querySelectorAll('form').forEach((form) =>
  form.addEventListener('submit', async (event) => {
    event.preventDefault();
    const status = document.getElementById('diagnostics');
    try {
      if (form.id === 'designer') document.getElementById('payload').value = policy();
      const response = await fetch(
        event.submitter?.getAttribute('formaction') || form.getAttribute('action'),
        { method: 'POST', body: new FormData(form) },
      );
      if (response.redirected) {
        location.href = response.url;
        return;
      }
      if (response.headers.get('content-type')?.includes('application/problem+json')) {
        const problem = await response.json();
        status.textContent = [
          `${problem.code}: ${problem.title}`,
          ...(problem.diagnostics ?? []).map((d) => `${d.path || '(root)'}: ${d.code}`),
          `Trace: ${problem.traceId}`,
        ].join('\n');
      } else status.textContent = await response.text();
    } catch (error) {
      status.textContent = error.message;
    }
  }),
);
