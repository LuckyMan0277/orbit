import MarkdownIt from 'markdown-it';

const safeLink = value => /^(https?:|mailto:|#|file:\/\/|\/?[A-Za-z]:[\\/]|\.\.?[\\/]|[^:/?#]+(?:[/?#].*)?$)/i.test(value);
const markdown = new MarkdownIt({ html:false, linkify:false, breaks:false, typographer:true });
markdown.validateLink = safeLink;
const slug = text => text.toLowerCase().replace(/<[^>]*>/g,'').trim().replace(/[^\p{L}\p{N}_-]+/gu,'-').replace(/(^-|-$)/g,'');
const originalHeading = markdown.renderer.rules.heading_open || ((tokens,index,options,_,self)=>self.renderToken(tokens,index,options));
markdown.renderer.rules.heading_open = (tokens,index,options,env,self) => { tokens[index].attrSet('id',slug(tokens[index+1]?.content||'')); return originalHeading(tokens,index,options,env,self); };
const originalLink = markdown.renderer.rules.link_open || ((tokens,index,options,_,self)=>self.renderToken(tokens,index,options));
markdown.renderer.rules.link_open = (tokens,index,options,env,self) => {
  const token=tokens[index],href=token.attrGet('href')||'';
  token.attrSet('data-md-link',href);token.attrSet('href','#');
  return originalLink(tokens,index,options,env,self);
};
markdown.renderer.rules.image = (tokens,index) => {
  const token=tokens[index],src=token.attrGet('src')||'',alt=markdown.utils.escapeHtml(token.content||src);
  return safeLink(src)?`<span class="md-image-link">🖼 <a href="#" data-md-link="${markdown.utils.escapeHtml(src)}">${alt||markdown.utils.escapeHtml(src)}</a></span>`:`<span class="md-image-link">🖼 ${alt}</span>`;
};
markdown.renderer.rules.fence = (tokens,index) => {
  const token=tokens[index],language=token.info.trim().split(/\s+/)[0];
  return `<div class="md-code"><button data-md-copy="${encodeURIComponent(token.content)}">복사</button><pre><code class="language-${markdown.utils.escapeHtml(language)}">${markdown.utils.escapeHtml(token.content)}</code></pre></div>`;
};
export function renderMarkdown(source) {
  return markdown.render(source).replace(/<li>\s*\[ \]\s*/g,'<li><input type="checkbox" disabled> ').replace(/<li>\s*\[x\]\s*/gi,'<li><input type="checkbox" checked disabled> ');
}
