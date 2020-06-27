class Footer < HyperComponent
  render(DIV, class: "Footer") do
    SPAN(style: {width: "auto"}){
      SPAN {
        "Help us improve this site: "
      }
      A(href: "https://github.com/Revolutionary-Games/ThriveDevCenter") {
        "Visit this project on Github"
      }
    }
  end
end
