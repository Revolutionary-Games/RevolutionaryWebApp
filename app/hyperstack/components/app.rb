# app/hyperstack/component/app.rb

# This is your top level component, the rails router will
# direct all requests to mount this component.  You may
# then use the Route psuedo component to mount specific
# subcomponents depending on the URL.

class App < HyperComponent
  include Hyperstack::Router

  render(DIV) do

    DIV do
      UL do
        LI { Link('/') { 'Home' } }
        LI { Link('/symbols') { 'Symbols' } }
        # LI { Link('/about') { 'About' } }        
      end
    end

    Switch do
      Route('/symbols', mounts: Symbols)
      Route('/', exact: true, mounts: Home)
    end
  end
end
