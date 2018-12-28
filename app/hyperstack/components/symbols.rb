
class Symbols < HyperComponent

  

  render(DIV) do
    
    DIV { "total symbols: #{DebugSymbol.count}" }
    # DIV do
    #   DIV { @entry.word }
    #   DIV { @entry.pronunciation }
    #   DIV { @entry.definition }
    #   BUTTON { 'pick another' }.on(:click) { pick_entry! }
    # end
    
    
    # DIV(class: 'green-text') { "Let's gets started!" }
  end
  
end
