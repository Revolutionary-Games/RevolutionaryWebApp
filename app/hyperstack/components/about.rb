class About < HyperComponent

  render(DIV) do
    H1 { "About" }

    P {
      "This is a website for handling Thrive crash information."
    }

    P {
      "This site uses cookies to keep track of Rails sessions."
    }
  end
end
    
