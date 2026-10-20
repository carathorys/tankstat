import Box from '@mui/material/Box'

/**
 * The settings at fault, as the server named them (a refused save or restart, the file left out at start); nothing when there are none. On
 * its own, so the Backups page lists a refused save of its keys without loading the settings form.
 */
export function ProblemList({ problems }: { problems: readonly string[] }) {
  if (problems.length === 0) return null
  return (
    <Box component="ul" sx={{ m: 0, pl: 2.5 }}>
      {problems.map((p) => (
        <li key={p}>{p}</li>
      ))}
    </Box>
  )
}
